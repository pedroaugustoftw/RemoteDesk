"""Bounded, explicitly authorized file transfer compatible with the .NET client."""
import asyncio
import base64
import hashlib
import os
import re
import uuid
from datetime import datetime, timezone
from pathlib import Path

LIMIT = 2 * 1024**3


def checked_path(value):
    if not isinstance(value, str) or len(value) > 2048 or "\0" in value:
        raise ValueError("Caminho inválido.")
    path = Path(value)
    if not path.is_absolute():
        raise ValueError("Informe um caminho absoluto.")
    if os.name == "nt" and (value.startswith("\\\\") or ":" in value[2:]):
        raise ValueError("Caminho de rede ou dispositivo não permitido.")
    # Do not follow symlinks/reparse points into unintended locations.
    for part in (path, *path.parents):
        if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
            raise ValueError("Links não são permitidos.")
    return path


def valid_name(name):
    return isinstance(name, str) and 0 < len(name) <= 255 and name not in (".", "..") and not any(
        ord(c) < 32 or c in '\\/:*?"<>|' for c in name)


def listing(value):
    if not value:
        roots = [Path(f"{c}:/") for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ" if Path(f"{c}:/").exists()] if os.name == "nt" else [Path("/")]
        return [{"name": ("Unidade " + p.drive[0] if os.name == "nt" else "Sistema"),
                 "path": str(p), "directory": True, "size": 0} for p in roots]
    path = checked_path(value)
    result = []
    for child in sorted(path.iterdir(), key=lambda p: (not p.is_dir(), p.name.casefold())):
        if not valid_name(child.name):
            continue
        try:
            checked_path(str(child))
            stat = child.stat()
            if not (child.is_file() or child.is_dir()):
                continue
            result.append({"name": child.name, "path": str(child), "directory": child.is_dir(),
                "size": stat.st_size if child.is_file() else 0,
                "modified": datetime.fromtimestamp(stat.st_mtime, timezone.utc).isoformat()})
        except (OSError, ValueError):
            continue
    return result


class FileTransfer:
    def __init__(self, emit, choose, report, host):
        self.emit, self.choose, self.report, self.host = emit, choose, report, host
        self.incoming, self.waiters, self.downloads, self.tasks = {}, {}, {}, set()
        self.uploads = {}

    async def send(self, kind, tid, **values):
        await self.emit({"kind": kind, "transferId": tid, **values})

    def start(self, coroutine):
        task = asyncio.create_task(coroutine)
        self.tasks.add(task)
        task.add_done_callback(self.tasks.discard)
        return task

    async def upload(self, source, destination=None, tid=None, replace=False):
        tid = tid or uuid.uuid4().hex
        ready, done = asyncio.Future(), asyncio.Future()
        self.waiters[tid] = (ready, done)
        self.uploads[tid] = asyncio.current_task()
        try:
            path = checked_path(source)
            if not path.is_file() or not valid_name(path.name):
                raise ValueError("Arquivo inválido.")
            # Hold the same file handle through hashing/transmission; receiver verifies exact size/hash.
            with path.open("rb") as stream:
                size = os.fstat(stream.fileno()).st_size
                if size > LIMIT:
                    raise ValueError("Limite: 2 GiB por arquivo.")
                digest = await asyncio.to_thread(hashlib.file_digest, stream, "sha256")
                stream.seek(0)
                await self.send("offer", tid, name=path.name, size=size, hash=digest.hexdigest().upper(),
                    path=destination, replace=replace)
                if not await asyncio.wait_for(ready, 120):
                    raise ValueError("Destino recusou o arquivo.")
                offset = 0
                while block := await asyncio.to_thread(stream.read, 65536):
                    await self.send("chunk", tid, chunk=base64.b64encode(block).decode(), offset=offset)
                    offset += len(block)
                    self.report(f"Enviando {path.name}: {offset * 100 // max(size, 1)}%")
                await self.send("complete", tid)
                if not await asyncio.wait_for(done, 600):
                    raise ValueError("Destino não confirmou a integridade.")
                self.report("Arquivo enviado e SHA-256 confirmado.")
        except (Exception, asyncio.CancelledError) as ex:
            self.report(str(ex) or "Transferência cancelada.")
            try:
                await self.send("cancel", tid)
            except Exception:
                pass
        finally:
            self.waiters.pop(tid, None)
            self.uploads.pop(tid, None)

    async def download(self, remote, local):
        if len(self.downloads) >= 16:
            raise ValueError("Aguarde os downloads pendentes.")
        tid = uuid.uuid4().hex
        self.downloads[tid] = checked_path(local)
        await self.send("pull", tid, path=remote)

    def drop(self, tid):
        item = self.incoming.pop(tid, None)
        if item:
            item[0].close()
            item[1].unlink(missing_ok=True)

    async def receive(self, f):
        tid, kind = f.get("transferId", ""), f.get("kind")
        if not re.fullmatch(r"[a-fA-F0-9]{32}", tid):
            raise ValueError("Identificador de arquivo inválido.")
        if kind in ("browse", "mkdir", "pull") and not self.host:
            raise ValueError("Comando de arquivo em direção inválida.")
        try:
            if kind in ("browse", "mkdir", "pull"):
                if not self.host:
                    raise ValueError("Comando de arquivo em direção inválida.")
                if kind == "pull":
                    self.start(self.upload(f["path"], tid=tid))
                else:
                    if kind == "mkdir":
                        checked_path(f["path"]).mkdir()
                        await self.send("listing", tid, path=f["path"], entries=[])
                        return
                    entries = await asyncio.to_thread(listing, f.get("path", ""))
                    for offset in range(0, max(1, len(entries)), 100):
                        await self.send("listing", tid, path=f.get("path", ""), entries=entries[offset:offset+100],
                                        more=offset+100 < len(entries))
            elif kind == "offer":
                name, size, digest = f.get("name"), f.get("size", -1), f.get("hash", "")
                if len(self.incoming) >= 2 or tid in self.incoming or not valid_name(name) or not isinstance(size, int) or not 0 <= size <= LIMIT or not re.fullmatch("[0-9a-fA-F]{64}", digest):
                    raise ValueError("Oferta de arquivo inválida.")
                destination = self.downloads.pop(tid, None)
                if destination is None and self.host and f.get("path"):
                    destination = checked_path(f["path"])
                    if destination.name != name:
                        raise ValueError("Nome e destino não correspondem.")
                if destination is None:
                    answer = await self.choose(name, size)
                    if not answer:
                        await self.send("reject", tid)
                        return
                    destination = checked_path(answer)
                if destination.exists():
                    # Never overwrite automatically, even if an older peer requests Replace.
                    raise ValueError("Arquivo já existe. Escolha outro nome.")
                temp = destination.with_name(destination.name + ".remotedesk-" + uuid.uuid4().hex + ".part")
                stream = temp.open("xb")
                self.incoming[tid] = (stream, temp, destination, size, digest.lower(), hashlib.sha256())
                await self.send("accept", tid)
            elif kind == "chunk":
                item = self.incoming.get(tid)
                if item is None:
                    return
                if not isinstance(f.get("chunk"), str) or len(f["chunk"]) > 87384:
                    raise ValueError("Bloco inválido.")
                block = base64.b64decode(f["chunk"], validate=True)
                stream, _, _, size, _, digest = item
                if not 0 < len(block) <= 65536 or f.get("offset", 0) != stream.tell() or stream.tell()+len(block) > size:
                    raise ValueError("Tamanho ou posição de bloco inválido.")
                stream.write(block)
                digest.update(block)
                self.report(f"Recebendo: {stream.tell()*100//max(size, 1)}%")
            elif kind == "complete":
                stream, temp, destination, size, expected, digest = self.incoming[tid]
                if stream.tell() != size or digest.hexdigest() != expected:
                    raise ValueError("Integridade SHA-256 inválida.")
                stream.flush()
                os.fsync(stream.fileno())
                stream.close()
                # Atomic creation without overwriting an existing destination.
                os.link(temp, destination)
                temp.unlink()
                self.incoming.pop(tid)
                await self.send("verified", tid)
                self.report("Arquivo recebido e SHA-256 verificado.")
            elif kind in ("accept", "reject", "verified", "cancel"):
                if kind in ("cancel", "reject"):
                    self.drop(tid)
                    self.downloads.pop(tid, None)
                    upload = self.uploads.get(tid)
                    if upload and upload is not asyncio.current_task():
                        upload.cancel()
                pair = self.waiters.get(tid)
                if pair:
                    future = pair[1 if kind == "verified" else 0]
                    if not future.done():
                        future.set_result(kind in ("accept", "verified"))
                    if kind in ("reject", "cancel") and not pair[1].done():
                        pair[1].set_result(False)
        except Exception as ex:
            self.drop(tid)
            self.downloads.pop(tid, None)
            self.report(str(ex))
            await self.send("listing" if kind in ("browse", "mkdir") else "cancel", tid,
                            path=f.get("path", ""), entries=[] if kind in ("browse", "mkdir") else None,
                            error=str(ex)[:512])

    def close(self):
        for upload in list(self.uploads.values()):
            if upload is not asyncio.current_task():
                upload.cancel()
        for task in list(self.tasks):
            task.cancel()
        for tid in list(self.incoming):
            self.drop(tid)
        for pair in self.waiters.values():
            for future in pair:
                if not future.done():
                    future.cancel()
        self.downloads.clear()

    async def cancel(self):
        for tid in set(self.incoming) | set(self.waiters) | set(self.downloads):
            await self.send("cancel", tid)
        self.close()
