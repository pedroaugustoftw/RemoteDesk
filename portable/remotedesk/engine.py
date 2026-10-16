import asyncio
import base64
import io
import json
import os
import socket
import sys
import time
from urllib.parse import urlsplit

from PIL import Image, ImageGrab
from .transport import open_socket, register, send, password_proof
from .files import FileTransfer


def host_supported():
    return sys.platform != "linux" or (bool(os.environ.get("DISPLAY")) and not os.environ.get("WAYLAND_DISPLAY"))


def grab_frame(level):
    image = ImageGrab.grab().convert("RGB")
    width = int(800 + 1120 * level / 100)
    image.thumbnail((width, 2160))
    output = io.BytesIO()
    image.save(output, format="JPEG", quality=int(30 + level * .6))
    data = output.getvalue()
    if len(data) > 1400000:
        raise ValueError("Quadro excede o limite; reduza a qualidade.")
    return base64.b64encode(data).decode()


def key_name(vk):
    special = {8: "backspace", 9: "tab", 13: "enter", 16: "shift", 17: "ctrl", 18: "alt",
        19: "pause", 20: "capslock", 27: "esc", 32: "space", 33: "pageup", 34: "pagedown",
        35: "end", 36: "home", 37: "left", 38: "up", 39: "right", 40: "down", 45: "insert",
        46: "delete", 91: "command" if sys.platform == "darwin" else "win",
        92: "command" if sys.platform == "darwin" else "win", 186: ";", 187: "=", 188: ",",
        189: "-", 190: ".", 191: "/", 192: "`", 219: "[", 220: "\\", 221: "]", 222: "'"}
    if 48 <= vk <= 90:
        return chr(vk).lower()
    if 112 <= vk <= 135:
        return "f" + str(vk - 111)
    return special.get(vk)


class Engine:
    def __init__(self, config, key, event, ask):
        self.config, self.key, self.event, self.ask = config, key, event, ask
        self.signal = self.media = self.session = self.files = None
        self.host = False
        self.permissions = {}
        self.level = 60
        self.jobs = set()
        self.pressed_keys, self.pressed_buttons = set(), set()
        self.last_input = 0
        self.held_since = 0
        self.password_challenge = None
        self.running = True

    def start_job(self, coro):
        task = asyncio.create_task(coro)
        self.jobs.add(task)
        task.add_done_callback(self.jobs.discard)
        task.add_done_callback(self.job_done)
        return task

    def job_done(self, task):
        if not task.cancelled() and task.exception():
            self.event("error", str(task.exception()))

    async def run(self):
        while self.running:
            try:
                self.signal = await open_socket(self.config["server"], self.config.get("sha256", ""))
                ready = await register(self.signal, self.key, socket.gethostname())
                self.event("ready", ready["deviceId"])
                await send(self.signal, {"type": "access-policy", "passwordAccess": False})
                heartbeat = asyncio.create_task(self.heartbeat())
                try:
                    async for raw in self.signal:
                        await self.on_signal(json.loads(raw))
                finally:
                    heartbeat.cancel()
                    await asyncio.gather(heartbeat, return_exceptions=True)
            except asyncio.CancelledError:
                break
            except Exception as ex:
                self.event("error", str(ex))
            finally:
                await self.cleanup()
                if self.signal:
                    await self.signal.close()
                self.signal = None
                self.event("offline", None)
            if self.running:
                await asyncio.sleep(5)

    async def heartbeat(self):
        while True:
            await send(self.signal, {"type": "heartbeat"})
            if self.media:
                await send(self.media, {"type": "heartbeat"})
            await asyncio.sleep(15)

    async def on_signal(self, m):
        kind = m.get("type")
        if kind == "incoming":
            if not host_supported() or self.session:
                await send(self.signal, {"type": "reject", "sessionId": m["sessionId"]})
                self.event("error", "Receber sessões exige X11 no Linux; Wayland pode ser usado como visualizador.")
            else:
                self.session = m["sessionId"]
                self.start_job(self.consent(m))
        elif kind == "pending":
            self.session = m["sessionId"]
            self.event("status", "Aguardando autorização do destino…")
        elif kind == "password-challenge":
            self.password_challenge = m
            self.start_job(self.password(m))
        elif kind == "started":
            await self.cleanup(clear_session=False)
            self.session, self.host = m["sessionId"], bool(m.get("isHost"))
            self.permissions = m.get("permissions") or {}
            self.files = FileTransfer(self.send_file, lambda n, s: self.ask("save", {"name": n, "size": s}),
                                      lambda text: self.event("progress", text), self.host)
            # Construct the media connection before exposing the viewer controls.
            if m.get("relayUrl"):
                target = self.config.get("relay") or m["relayUrl"]
                same_server = urlsplit(target).hostname == urlsplit(self.config["server"]).hostname
                pin = self.config.get("relaySha256", self.config.get("sha256", "") if same_server else "")
                self.media = await open_socket(target, pin)
                await send(self.media, {"type": "attach", "sessionId": self.session, "payload": m["relayTicket"]})
                attached = json.loads(await asyncio.wait_for(self.media.recv(), 12))
                if attached.get("type") != "attached":
                    raise ValueError("Relay recusou a sessão.")
                self.start_job(self.read_media())
            self.event("started", m)
            if self.host:
                self.start_job(self.capture())
                self.start_job(self.input_watchdog())
        elif kind in ("ended", "rejected", "denied", "cancelled"):
            await self.cleanup()
            self.event("status", m.get("error") or "Sessão encerrada ou recusada.")
        elif kind in ("error", "password-error"):
            self.event("error", m.get("error") or "Servidor recusou a operação.")
        elif kind in ("frame", "input", "file", "quality", "probe", "probe-reply"):
            await self.on_media(m)

    async def consent(self, m):
        answer = await self.ask("consent", m)
        if self.session != m["sessionId"]:
            return
        await send(self.signal, {"type": "respond" if answer else "reject", "sessionId": self.session,
                                "permissions": answer or {"view": False}})

    async def password(self, m):
        answer = await self.ask("password", m)
        if self.session != m["sessionId"]:
            return
        if answer:
            await send(self.signal, {"type": "password-proof", "sessionId": self.session,
                "payload": password_proof(m["publicKey"], self.session, m["nonce"], answer)})
        else:
            await self.end()

    async def request(self, target):
        if not self.signal or self.session:
            raise ValueError("Aguarde o registro ou encerre a sessão atual.")
        if len(target) != 9 or not target.isascii() or not target.isdigit():
            raise ValueError("Informe o ID de 9 dígitos.")
        await send(self.signal, {"type": "request", "targetId": target,
            "permissions": {"view": True, "control": True, "files": True}})

    async def read_media(self):
        try:
            async for raw in self.media:
                await self.on_media(json.loads(raw))
        finally:
            if self.session:
                await self.end()

    async def emit(self, kind, **data):
        if not self.session:
            return
        await send(self.media or self.signal, {"type": kind, "sessionId": self.session, **data})

    async def send_file(self, f):
        await self.emit("file", file=f)

    async def on_media(self, m):
        if not self.session or m.get("sessionId") != self.session:
            return
        kind = m.get("type")
        if kind == "frame" and not self.host and self.permissions.get("view"):
            data = base64.b64decode(m["payload"], validate=True)
            if len(data) > 1400000:
                raise ValueError("Quadro inválido.")
            # Validate dimensions before Qt decodes the remote image.
            with Image.open(io.BytesIO(data)) as image:
                if image.format != "JPEG" or max(image.size) > 4096:
                    raise ValueError("Imagem remota inválida.")
            self.event("frame", data)
        elif kind == "input" and self.host and self.permissions.get("control"):
            self.apply_input(m.get("data") or {})
        elif kind == "quality" and self.host:
            level = m.get("qualityLevel")
            if isinstance(level, int) and 0 <= level <= 100:
                self.level = level
        elif kind == "probe" and self.host:
            await self.emit("probe-reply", payload=m.get("payload"))
        elif kind == "file" and self.permissions.get("files") and self.files:
            f = m.get("file") or {}
            if f.get("kind") == "listing":
                if not self.host:
                    self.event("listing", f)
            else:
                await self.files.receive(f)

    async def capture(self):
        try:
            while self.session and self.host:
                payload = await asyncio.to_thread(grab_frame, self.level)
                await self.emit("frame", payload=payload)
                await asyncio.sleep(1 / (20 - 12 * self.level / 100))
        except asyncio.CancelledError:
            raise
        except Exception as ex:
            self.event("error", "Captura interrompida: " + str(ex))
            await self.end()

    def apply_input(self, d):
        import pyautogui as pg
        kind, x, y, key = d.get("kind"), d.get("x", 0), d.get("y", 0), d.get("key", 0)
        if not isinstance(x, (int, float)) or not isinstance(y, (int, float)) or not 0 <= x <= 1 or not 0 <= y <= 1:
            raise ValueError("Coordenadas inválidas.")
        self.last_input = time.monotonic()
        pg.PAUSE = 0
        if kind in ("move", "button", "wheel"):
            width, height = pg.size()
            pg.moveTo(round(x * (width - 1)), round(y * (height - 1)))
        if kind == "button":
            button = {0: "left", 1: "right", 2: "middle"}.get(d.get("button", 0))
            if button:
                (pg.mouseDown if d.get("down") else pg.mouseUp)(button=button)
                (self.pressed_buttons.add if d.get("down") else self.pressed_buttons.discard)(button)
        elif kind == "wheel":
            delta = d.get("delta", 0)
            if not isinstance(delta, int) or abs(delta) > 12000:
                raise ValueError("Rolagem inválida.")
            pg.scroll(int(delta / 120))
        elif kind == "key" and isinstance(key, int):
            name = key_name(key)
            if name:
                (pg.keyDown if d.get("down") else pg.keyUp)(name)
                (self.pressed_keys.add if d.get("down") else self.pressed_keys.discard)(name)

    def release_input(self):
        if not (self.pressed_keys or self.pressed_buttons):
            return
        import pyautogui as pg
        previous = pg.FAILSAFE
        try:
            # Only release keys/buttons already pressed by this authorized session.
            pg.FAILSAFE = False
            for key in self.pressed_keys:
                pg.keyUp(key)
            for button in self.pressed_buttons:
                pg.mouseUp(button=button)
        finally:
            self.pressed_keys.clear()
            self.pressed_buttons.clear()
            pg.FAILSAFE = previous

    async def input_watchdog(self):
        while self.session:
            await asyncio.sleep(1)
            if time.monotonic() - self.last_input > 5:
                self.release_input()

    async def cleanup(self, clear_session=True):
        if clear_session:
            self.session = None
        current = asyncio.current_task()
        jobs = [job for job in self.jobs if job is not current]
        for job in jobs:
            job.cancel()
        if jobs:
            await asyncio.gather(*jobs, return_exceptions=True)
        if self.files:
            self.files.close()
            self.files = None
        self.release_input()
        media, self.media = self.media, None
        if media:
            await media.close()
        self.permissions = {}
        self.event("ended", None)

    async def end(self):
        session = self.session
        await self.cleanup()
        if session and self.signal:
            await send(self.signal, {"type": "end", "sessionId": session})

    async def stop(self):
        self.running = False
        await self.end()
        if self.signal:
            await self.signal.close()
