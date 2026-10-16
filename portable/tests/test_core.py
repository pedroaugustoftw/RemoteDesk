import asyncio
import base64
import hashlib
import os
from pathlib import Path
import tempfile
import unittest
import uuid

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding
from remotedesk.transport import (tls_options, new_key, sign_nonce, export_key, import_key,
    password_proof, public_key)
from remotedesk.files import FileTransfer, checked_path, listing, valid_name
from remotedesk.engine import key_name


class Security(unittest.TestCase):
    def test_tls_rejects_insecure_lan_and_bad_pins(self):
        for url, pin in [("ws://192.168.1.2/ws", ""), ("https://host/ws", ""),
                         ("wss://host/ws", "x"*64), ("wss://user:pass@host/ws", "")]:
            with self.assertRaises(ValueError):
                tls_options(url, pin)
        self.assertIsNotNone(tls_options("wss://example.com/ws"))
        self.assertIsNone(tls_options("ws://127.0.0.1/ws"))

    def test_rsa_pss_dotnet_compatible_salt_and_key_roundtrip(self):
        key = import_key(export_key(new_key()))
        nonce = os.urandom(32)
        signature = sign_nonce(key, base64.b64encode(nonce).decode())
        key.public_key().verify(base64.b64decode(signature), nonce,
            padding.PSS(mgf=padding.MGF1(hashes.SHA256()), salt_length=32), hashes.SHA256())

    def test_password_proof_is_bound_to_session_nonce(self):
        key = new_key()
        session, nonce = uuid.uuid4().hex, base64.b64encode(os.urandom(32)).decode()
        proof = password_proof(public_key(key), session, nonce, "test password")
        clear = key.decrypt(base64.b64decode(proof), padding.OAEP(mgf=padding.MGF1(hashes.SHA256()), algorithm=hashes.SHA256(), label=None))
        self.assertEqual(clear.decode(), f"{session}\n{nonce}\ntest password")

    def test_path_and_name_rules(self):
        for name in ("../a", "a/b", ".", "..", "a:b", "bad\0name"):
            self.assertFalse(valid_name(name))
        with self.assertRaises(ValueError):
            checked_path("relative")
        self.assertTrue(valid_name("foto.png"))
        self.assertEqual(key_name(17), "ctrl")


class Transfers(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.messages = []
        self.queue_a, self.queue_b = asyncio.Queue(), asyncio.Queue()
        async def choose(name, size):
            return str(self.root/"destination"/name)
        (self.root/"destination").mkdir()
        async def to_a(f):
            await self.queue_a.put(f)
        async def to_b(f):
            self.messages.append(f)
            await self.queue_b.put(f)
        self.a = FileTransfer(to_b, choose, lambda s: None, False)
        self.b = FileTransfer(to_a, choose, lambda s: None, True)
        async def pump(queue, target):
            while True:
                await target.receive(await queue.get())
        self.pumps = [asyncio.create_task(pump(self.queue_a,self.a)),asyncio.create_task(pump(self.queue_b,self.b))]

    async def asyncTearDown(self):
        self.a.close()
        self.b.close()
        for task in self.pumps:
            task.cancel()
        await asyncio.gather(*self.pumps,return_exceptions=True)
        self.temp.cleanup()

    async def test_multichunk_binary_roundtrip_and_no_overwrite(self):
        path = self.root/"picture.png"
        data = os.urandom(150000)
        path.write_bytes(data)
        await asyncio.wait_for(self.a.upload(str(path)),10)
        self.assertEqual((self.root/"destination"/path.name).read_bytes(),data)
        self.assertEqual(len([f for f in self.messages if f["kind"]=="chunk"]),3)
        path.write_bytes(b"changed")
        await asyncio.wait_for(self.a.upload(str(path)),10)
        self.assertEqual((self.root/"destination"/path.name).read_bytes(),data)

    async def test_bad_offset_cleans_temporary_file(self):
        tid = uuid.uuid4().hex
        await self.b.receive({"kind":"offer","transferId":tid,"name":"bad.bin","size":3,"hash":hashlib.sha256(b"abc").hexdigest()})
        await self.b.receive({"kind":"chunk","transferId":tid,"chunk":"YWJj","offset":2})
        self.assertFalse(list((self.root/"destination").iterdir()))
        self.assertNotIn(tid,self.b.incoming)

    async def test_hash_mismatch_not_committed(self):
        tid=uuid.uuid4().hex
        await self.b.receive({"kind":"offer","transferId":tid,"name":"bad.bin","size":3,"hash":"0"*64})
        await self.b.receive({"kind":"chunk","transferId":tid,"chunk":"YWJj","offset":0})
        await self.b.receive({"kind":"complete","transferId":tid})
        self.assertFalse(list((self.root/"destination").iterdir()))

    async def test_cancel_cleans_partial(self):
        tid=uuid.uuid4().hex
        await self.b.receive({"kind":"offer","transferId":tid,"name":"cancel.bin","size":3,"hash":"0"*64})
        await self.b.receive({"kind":"cancel","transferId":tid})
        self.assertFalse(list((self.root/"destination").iterdir()))

    async def test_listing_pages_and_direction(self):
        for i in range(105):
            (self.root/f"{i}.txt").touch()
        self.assertGreater(len(listing(str(self.root))),100)
        with self.assertRaises(ValueError):
            await self.a.receive({"kind":"browse","transferId":uuid.uuid4().hex,"path":str(self.root)})


if __name__ == "__main__":
    unittest.main()
