"""Real Python/.NET broker+relay integration; uses an isolated registry and random ports."""
import asyncio
import base64
import io
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import uuid

from PIL import Image
from remotedesk.transport import new_key, open_socket, register, send


def port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1",0))
        return sock.getsockname()[1]


async def read(ws, kind):
    while True:
        m=json.loads(await asyncio.wait_for(ws.recv(),10))
        if m["type"]==kind:
            return m
        if m["type"]=="error":
            raise AssertionError(m)


async def exercise(url, root):
    sockets=[]
    try:
        a,b=await open_socket(url),await open_socket(url)
        sockets.extend([a,b])
        host_key=new_key()
        host=(await register(b,host_key,"Portable host"))["deviceId"]
        await register(a,new_key(),"Portable viewer")
        await send(a,{"type":"request","targetId":host,"permissions":{"view":True,"control":True,"files":True}})
        pending=await read(a,"pending")
        incoming=await read(b,"incoming")
        # Real refusal and retry, before media is authorized.
        await send(b,{"type":"reject","sessionId":incoming["sessionId"]})
        await read(a,"ended")
        await read(b,"ended")
        await send(a,{"type":"request","targetId":host,"permissions":{"view":True,"control":True,"files":True}})
        pending=await read(a,"pending")
        incoming=await read(b,"incoming")
        await send(b,{"type":"respond","sessionId":incoming["sessionId"],"permissions":{"view":True,"control":False,"files":True}})
        va,hb=await read(a,"started"),await read(b,"started")
        assert not va["permissions"]["control"]
        v,h=await open_socket(va["relayUrl"]),await open_socket(hb["relayUrl"])
        sockets.extend([v,h])
        for ws,m in [(v,va),(h,hb)]:
            await send(ws,{"type":"attach","sessionId":m["sessionId"],"payload":m["relayTicket"]})
            await read(ws,"attached")
        session=va["sessionId"]
        image=Image.new("RGB",(80,60),"blue")
        output=io.BytesIO()
        image.save(output,"JPEG")
        await send(h,{"type":"frame","sessionId":session,"payload":base64.b64encode(output.getvalue()).decode()})
        assert base64.b64decode((await read(v,"frame"))["payload"])==output.getvalue()
        await send(v,{"type":"input","sessionId":session,"data":{"kind":"move","x":.5,"y":.5}})
        assert json.loads(await asyncio.wait_for(v.recv(),5))["type"]=="error"
        await send(v,{"type":"quality","sessionId":session,"qualityLevel":25})
        assert (await read(h,"quality"))["qualityLevel"]==25
        from remotedesk.files import FileTransfer
        source=root/"source.bin"
        source.write_bytes(os.urandom(180000))
        destination=root/"result.bin"
        async def file_v(f):
            await send(v,{"type":"file","sessionId":session,"file":f})
        async def file_h(f):
            await send(h,{"type":"file","sessionId":session,"file":f})
        async def choose(n,s):
            return str(destination)
        viewer=FileTransfer(file_v,choose,print,False)
        receiver=FileTransfer(file_h,choose,print,True)
        async def pump(ws,manager):
            while True:
                m=json.loads(await ws.recv())
                if m["type"]=="file":
                    await manager.receive(m["file"])
        pumps=[asyncio.create_task(pump(v,viewer)),asyncio.create_task(pump(h,receiver))]
        try:
            await asyncio.wait_for(viewer.upload(str(source)),15)
            assert destination.read_bytes()==source.read_bytes()
        finally:
            for p in pumps:p.cancel()
            await asyncio.gather(*pumps,return_exceptions=True)
            viewer.close();receiver.close()
        await send(a,{"type":"end","sessionId":session})
        await read(a,"ended");await read(b,"ended")
        await b.close()
        b=await open_socket(url)
        sockets.append(b)
        assert (await register(b,host_key,"Restart"))["deviceId"]==host
        print("PASS .NET broker+relay: RSA auth, persistent ID, reject/accept, JPEG, permission denial, quality, multichunk SHA-256 files, end")
    finally:
        for ws in sockets:
            await ws.close()


def main():
    root=Path(__file__).resolve().parents[2]
    dotnet=os.environ.get("DOTNET_EXE","dotnet")
    signal_port,media_port=port(),port()
    with tempfile.TemporaryDirectory() as temp:
        env={**os.environ,"RemoteDesk__RelaySecret":uuid.uuid4().hex+uuid.uuid4().hex}
        relay_env={**env,"ASPNETCORE_URLS":f"http://127.0.0.1:{media_port}"}
        server_env={**env,"ASPNETCORE_URLS":f"http://127.0.0.1:{signal_port}",
            "RemoteDesk__RelayUrl":f"ws://127.0.0.1:{media_port}/ws",
            "RemoteDesk__RelayApiUrl":f"http://127.0.0.1:{media_port}",
            "RemoteDesk__RegistryPath":str(Path(temp)/"registry.json")}
        processes=[]
        try:
            for name,settings in [("relay",relay_env),("server",server_env)]:
                dll=root/name/"bin/Release/net8.0"/("RemoteDesk.Relay.dll" if name=="relay" else "RemoteDesk.Server.dll")
                processes.append(subprocess.Popen([dotnet,str(dll)],cwd=temp,env=settings,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL))
            import time
            import urllib.request
            for _ in range(100):
                try:
                    for p in (signal_port,media_port):
                        urllib.request.urlopen(f"http://127.0.0.1:{p}/health",timeout=1).close()
                    break
                except OSError:time.sleep(.1)
            else:raise RuntimeError("Isolated services failed to start")
            asyncio.run(exercise(f"ws://127.0.0.1:{signal_port}/ws",Path(temp)))
        finally:
            for process in processes:
                process.terminate()
            for process in processes:
                process.wait(timeout=10)


if __name__=="__main__":main()
