import asyncio
import base64
import hashlib
import ipaddress
import json
import ssl
from urllib.parse import urlsplit

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from websockets.asyncio.client import connect

MAX_MESSAGE = 2 * 1024 * 1024


def tls_options(url, fingerprint=""):
    parsed = urlsplit(url)
    if parsed.username or parsed.password or parsed.fragment:
        raise ValueError("Endereço de servidor inválido.")
    if parsed.scheme == "ws":
        # Only local development may use plaintext. LAN always requires TLS.
        if parsed.hostname not in ("localhost", "127.0.0.1", "::1"):
            raise ValueError("Use wss:// para conexões fora deste computador.")
        return None
    if parsed.scheme != "wss" or not parsed.hostname:
        raise ValueError("Informe wss://servidor:porta/ws.")
    context = ssl.create_default_context()
    if fingerprint:
        if len(fingerprint) != 64 or any(c not in "0123456789abcdefABCDEF" for c in fingerprint):
            raise ValueError("SHA-256 TLS deve conter 64 caracteres hexadecimais.")
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE  # Exact DER pin is checked before sending any data.
    return context


async def open_socket(url, fingerprint=""):
    context = tls_options(url, fingerprint)
    socket = await connect(url, ssl=context, max_size=MAX_MESSAGE, max_queue=4,
                           open_timeout=12, close_timeout=3, proxy=None)
    if fingerprint:
        tls = socket.transport.get_extra_info("ssl_object")
        cert = tls.getpeercert(binary_form=True) if tls else b""
        if hashlib.sha256(cert).hexdigest().upper() != fingerprint.upper():
            await socket.close()
            raise ValueError("Certificado TLS não corresponde à SHA-256 configurada.")
    return socket


def new_key():
    return rsa.generate_private_key(public_exponent=65537, key_size=3072)


def export_key(key):
    return base64.b64encode(key.private_bytes(serialization.Encoding.DER,
        serialization.PrivateFormat.PKCS8, serialization.NoEncryption())).decode()


def import_key(value):
    return serialization.load_der_private_key(base64.b64decode(value, validate=True), None)


def public_key(key):
    return base64.b64encode(key.public_key().public_bytes(serialization.Encoding.DER,
        serialization.PublicFormat.SubjectPublicKeyInfo)).decode()


def sign_nonce(key, nonce):
    raw = base64.b64decode(nonce, validate=True)
    if len(raw) != 32:
        raise ValueError("Desafio de autenticação inválido.")
    return base64.b64encode(key.sign(raw, padding.PSS(mgf=padding.MGF1(hashes.SHA256()),
        salt_length=32), hashes.SHA256())).decode()


def password_proof(public, session, nonce, password):
    if len(session) != 32 or len(base64.b64decode(nonce, validate=True)) != 32:
        raise ValueError("Desafio de senha inválido.")
    if len(password.encode()) > 96:
        raise ValueError("Senha muito longa.")
    key = serialization.load_der_public_key(base64.b64decode(public, validate=True))
    if not isinstance(key, rsa.RSAPublicKey) or key.key_size < 2048:
        raise ValueError("Chave do destino inválida.")
    return base64.b64encode(key.encrypt(f"{session}\n{nonce}\n{password}".encode(),
        padding.OAEP(mgf=padding.MGF1(hashes.SHA256()), algorithm=hashes.SHA256(), label=None))).decode()


async def send(socket, message):
    data = json.dumps(message, separators=(",", ":"))
    if len(data.encode()) > MAX_MESSAGE:
        raise ValueError("Mensagem excede o limite.")
    await socket.send(data)


async def register(socket, key, name):
    await send(socket, {"type": "register", "name": name[:80], "publicKey": public_key(key)})
    challenge = json.loads(await asyncio.wait_for(socket.recv(), 15))
    if challenge.get("type") != "challenge":
        raise ValueError(challenge.get("error") or "Servidor recusou o registro.")
    await send(socket, {"type": "auth", "signature": sign_nonce(key, challenge["nonce"])})
    result = json.loads(await asyncio.wait_for(socket.recv(), 15))
    if result.get("type") != "ready":
        raise ValueError(result.get("error") or "Servidor recusou a autenticação.")
    return result
