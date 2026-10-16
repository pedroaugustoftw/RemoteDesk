"""Build on the destination OS, then package without losing Unix modes/symlinks."""
import hashlib
import importlib.metadata
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile

root=Path(__file__).resolve().parent.parent
os.chdir(root)
system={"Windows":"windows","Linux":"linux","Darwin":"macos"}[platform.system()]
arch="arm64" if platform.machine().lower() in ("arm64","aarch64") else "x64"
out=root/"artifacts"/"portable"/(system+"-"+arch)
stage=out/"dist"
icon=root/"branding/final/icons/remotedesk-app-icon-256.png"
args=[sys.executable,"-m","PyInstaller","--noconfirm","--clean","--windowed","--onedir",
      "--name","RemoteDesk","--paths","portable","--distpath",str(stage),"--workpath",str(out/"build"),
      "--specpath",str(out),"--collect-submodules","keyring.backends",
      "--hidden-import","pyautogui","--exclude-module","PySide6.QtWebEngineCore",
      "--exclude-module","PySide6.QtWebEngineWidgets","--exclude-module","PySide6.QtPdf",
      "--add-data",str(icon)+os.pathsep+"assets"]
if system=="windows":
    args += ["--icon",str(root/"branding/final/RemoteDesk.ico")]
elif system=="macos":
    from PIL import Image
    out.mkdir(parents=True,exist_ok=True)
    icns=out/"RemoteDesk.icns"
    Image.open(root/"branding/final/icons/icon-512.png").save(icns,format="ICNS")
    args += ["--icon",str(icns),"--osx-bundle-identifier","br.com.pedro.remotedesk"]
args += ["portable/launch.py"]
subprocess.run(args,check=True)
bundle=stage/("RemoteDesk.app/Contents/Resources" if system=="macos" else "RemoteDesk")
licenses=bundle/"THIRD_PARTY_LICENSES"
licenses.mkdir(parents=True,exist_ok=True)
for distribution in importlib.metadata.distributions():
    name=distribution.metadata.get("Name","dependency")
    for file in distribution.files or []:
        if any(part.lower() in ("licenses","license") for part in file.parts) or file.name.lower().startswith(("license","copying","notice")):
            source=distribution.locate_file(file)
            if source.is_file():
                destination=licenses/name/Path(*file.parts[1:])
                destination.parent.mkdir(parents=True,exist_ok=True)
                shutil.copy2(source,destination)
shutil.copy2(root/"THIRD_PARTY.md",licenses/"README.md")
for source in (root/"portable/licenses").glob("*"):
    shutil.copy2(source,licenses/source.name)
release=root/"artifacts/release"
release.mkdir(parents=True,exist_ok=True)
def smoke(executable):
    diagnostic=out/"startup.log"
    environment={**os.environ,"REMOTEDESK_DIAGNOSTICS":str(diagnostic)}
    try:
        subprocess.run([str(executable),"--smoke-test"],check=True,timeout=40,env=environment)
    except Exception:
        if diagnostic.exists():
            print(diagnostic.read_text(errors="replace"))
        raise
if system=="windows":
    exe=stage/"RemoteDesk/RemoteDesk.exe"
    smoke(exe)
    shutil.make_archive(str(release/f"RemoteDesk-{system}-{arch}-portable"),"zip",stage,"RemoteDesk")
elif system=="macos":
    app=stage/"RemoteDesk.app"
    # Resources were added after PyInstaller's ad-hoc signature; sign the final bundle again.
    subprocess.run(["codesign","--force","--deep","--sign","-",str(app)],check=True)
    smoke(app/"Contents/MacOS/RemoteDesk")
    # ditto preserves bundle symlinks/resources without requiring a mounted disk image on CI.
    archive=release/f"RemoteDesk-{system}-{arch}.zip"
    subprocess.run(["codesign","--verify","--deep","--strict",str(app)],check=True)
    subprocess.run(["ditto","-c","-k","--sequesterRsrc","--keepParent",str(app),str(archive)],check=True)
    unpack=out/"verify-archive"
    unpack.mkdir(exist_ok=True)
    subprocess.run(["ditto","-x","-k",str(archive),str(unpack)],check=True)
    restored=unpack/"RemoteDesk.app"
    subprocess.run(["codesign","--verify","--deep","--strict",str(restored)],check=True)
    smoke(restored/"Contents/MacOS/RemoteDesk")
else:
    bundle=stage/"RemoteDesk"
    shutil.copy2(root/"portable/install-linux.sh",bundle/"install-linux.sh")
    (bundle/"install-linux.sh").chmod(0o755)
    shutil.copy2(icon,bundle/"RemoteDesk.png")
    shutil.copy2(root/"docs/cross-platform.md",bundle/"LEIA-ME.md")
    (bundle/".remotedesk-owned").write_text("RemoteDesk portable 1\n")
    smoke(bundle/"RemoteDesk")
    with tarfile.open(release/f"RemoteDesk-{system}-{arch}.tar.gz","w:gz") as archive:
        archive.add(bundle,arcname="RemoteDesk")
print("Portable package:",system,arch)
