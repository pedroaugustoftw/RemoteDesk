import asyncio
import base64
import hashlib
import json
import os
from pathlib import Path
import sys
import uuid

from PySide6.QtCore import Qt, QThread, Signal, QStandardPaths, QRect, QCoreApplication, QEvent
from PySide6.QtGui import QPixmap, QPainter, QColor, QIcon
from PySide6.QtWidgets import (QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QLineEdit, QPushButton, QMessageBox, QDialog, QCheckBox, QDialogButtonBox,
    QFileDialog, QInputDialog, QSlider, QListWidget, QListWidgetItem, QSplitter)

from .engine import Engine, host_supported
from .transport import new_key, export_key, import_key
from .files import listing

STYLE = """
QWidget { background:#161616; color:#eeeeee; font-size:13px; }
QLineEdit,QListWidget { background:#222; border:1px solid #444; border-radius:5px; padding:8px; }
QPushButton { background:#1d6ff2; border:0; border-radius:5px; padding:9px 14px; }
QPushButton:disabled { background:#333; color:#888; }
QPushButton:hover { background:#3986ff; }
QSlider::groove:horizontal { height:5px; background:#444; }
QSlider::handle:horizontal { background:#1d6ff2; width:15px; margin:-5px 0; border-radius:7px; }
"""


def identity(root):
    import keyring
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
    from keyring.errors import KeyringError
    # A secure OS credential store is mandatory. Never silently fall back to plaintext.
    backend = keyring.get_keyring()
    if backend.priority <= 0 or "plaintext" in type(backend).__module__.lower():
        raise KeyringError("Ative o cofre do sistema (Keychain/Secret Service) e tente novamente.")
    account_file = root / "profile-id"
    if account_file.exists():
        account = account_file.read_text().strip()
    else:
        account = uuid.uuid4().hex
        account_file.write_text(account)
    secret = keyring.get_password("RemoteDesk.Portable", account)
    path = root / "identity.enc"
    if not secret:
        if path.exists():
            raise KeyringError("Chave do cofre indisponível. Desbloqueie o cofre para manter seu ID.")
        secret = base64.b64encode(os.urandom(32)).decode()
        keyring.set_password("RemoteDesk.Portable", account, secret)
    cipher = AESGCM(base64.b64decode(secret, validate=True))
    if path.exists():
        blob = path.read_bytes()
        return import_key(cipher.decrypt(blob[:12], blob[12:], account.encode()).decode())
    key = new_key()
    nonce = os.urandom(12)
    blob = nonce + cipher.encrypt(nonce, export_key(key).encode(), account.encode())
    with path.open("xb") as stream:
        stream.write(blob)
    path.chmod(0o600)
    return key


class Worker(QThread):
    event = Signal(str, object)
    question = Signal(str, str, object)

    def __init__(self, config, key):
        super().__init__()
        self.config, self.key = config, key
        self.loop = self.engine = None
        self.questions = {}

    def run(self):
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.engine = Engine(self.config, self.key, self.event.emit, self.ask)
        try:
            self.loop.run_until_complete(self.engine.run())
        finally:
            for future in self.questions.values():
                if not future.done():
                    future.cancel()
            pending = asyncio.all_tasks(self.loop)
            for task in pending:
                task.cancel()
            self.loop.run_until_complete(asyncio.gather(*pending, return_exceptions=True))
            self.loop.run_until_complete(self.loop.shutdown_default_executor())
            self.loop.close()

    async def ask(self, kind, payload):
        token = uuid.uuid4().hex
        future = asyncio.get_running_loop().create_future()
        self.questions[token] = future
        self.question.emit(token, kind, payload)
        try:
            return await asyncio.wait_for(future, 90)
        finally:
            self.questions.pop(token, None)

    def answer(self, token, value):
        def deliver():
            future = self.questions.get(token)
            if future and not future.done():
                future.set_result(value)
        if self.loop and not self.loop.is_closed():
            self.loop.call_soon_threadsafe(deliver)

    def call(self, action, *args, **kwargs):
        if not self.loop or not self.engine or not self.loop.is_running():
            return
        async def invoke():
            try:
                if action == "file":
                    if self.engine.files:
                        await getattr(self.engine.files, args[0])(*args[1:], **kwargs)
                else:
                    await getattr(self.engine, action)(*args, **kwargs)
            except Exception as ex:
                self.event.emit("error", str(ex))
        asyncio.run_coroutine_threadsafe(invoke(), self.loop)


class Screen(QWidget):
    def __init__(self, emit):
        super().__init__()
        self.emit, self.image, self.area = emit, QPixmap(), QRect()
        self.control = False
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setMouseTracking(True)
        self.setMinimumSize(320, 200)
        self.setCursor(Qt.CursorShape.CrossCursor)

    def frame(self, data):
        image = QPixmap()
        if image.loadFromData(data, "JPEG"):
            self.image = image
            self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.fillRect(self.rect(), QColor("#080808"))
        if not self.image.isNull():
            size = self.image.size().scaled(self.size(), Qt.AspectRatioMode.KeepAspectRatio)
            self.area = QRect((self.width()-size.width())//2, (self.height()-size.height())//2, size.width(), size.height())
            p.drawPixmap(self.area, self.image)

    def pointer(self, event, kind, **values):
        if self.control and self.area.contains(event.position().toPoint()):
            self.emit(kind=kind, x=min(1,max(0,(event.position().x()-self.area.x())/max(1,self.area.width()-1))),
                      y=min(1,max(0,(event.position().y()-self.area.y())/max(1,self.area.height()-1))), **values)

    def mouseMoveEvent(self, e):
        self.pointer(e, "move")

    def mousePressEvent(self, e):
        self.setFocus()
        self.pointer(e, "button", button={Qt.MouseButton.LeftButton:0, Qt.MouseButton.RightButton:1, Qt.MouseButton.MiddleButton:2}.get(e.button(),0), down=True)

    def mouseReleaseEvent(self, e):
        self.pointer(e, "button", button={Qt.MouseButton.LeftButton:0, Qt.MouseButton.RightButton:1, Qt.MouseButton.MiddleButton:2}.get(e.button(),0), down=False)

    def wheelEvent(self, e):
        self.pointer(e, "wheel", delta=e.angleDelta().y())

    @staticmethod
    def virtual_key(e):
        key = e.key()
        special = {Qt.Key.Key_Backspace:8, Qt.Key.Key_Tab:9, Qt.Key.Key_Return:13, Qt.Key.Key_Enter:13,
            Qt.Key.Key_Shift:16, Qt.Key.Key_Control:17, Qt.Key.Key_Alt:18, Qt.Key.Key_Escape:27,
            Qt.Key.Key_PageUp:33, Qt.Key.Key_PageDown:34, Qt.Key.Key_End:35, Qt.Key.Key_Home:36,
            Qt.Key.Key_Left:37, Qt.Key.Key_Up:38, Qt.Key.Key_Right:39, Qt.Key.Key_Down:40,
            Qt.Key.Key_Insert:45, Qt.Key.Key_Delete:46, Qt.Key.Key_Meta:91, Qt.Key.Key_CapsLock:20,
            59:186,61:187,44:188,45:189,46:190,47:191,96:192,91:219,92:220,93:221,39:222}
        if key in special:
            return special[key]
        if 32 <= key <= 90:
            return key
        if Qt.Key.Key_F1 <= key <= Qt.Key.Key_F24:
            return 112+key-Qt.Key.Key_F1
        return None

    def key_event(self, e, down):
        key = self.virtual_key(e)
        if self.control and key is not None:
            self.emit(kind="key", key=key, down=down)
            e.accept()

    def keyPressEvent(self, e):
        self.key_event(e, True)

    def keyReleaseEvent(self, e):
        if not e.isAutoRepeat():
            self.key_event(e, False)


class Files(QDialog):
    def __init__(self, owner):
        super().__init__(owner)
        self.owner, self.remote_path = owner, ""
        self.setWindowTitle("RemoteDesk — Transferência de arquivos")
        self.resize(960, 580)
        outer = QVBoxLayout(self)
        split = QSplitter()
        outer.addWidget(split)
        self.local_path = QLineEdit(str(Path.home()))
        self.remote_address = QLineEdit()
        self.local, self.remote = QListWidget(), QListWidget()
        self.remote.setSelectionMode(QListWidget.SelectionMode.ExtendedSelection)
        self.local.setSelectionMode(QListWidget.SelectionMode.ExtendedSelection)
        for title, address, view, refresh in [("Este computador", self.local_path, self.local, self.refresh_local),
                ("Computador remoto", self.remote_address, self.remote, self.refresh_remote)]:
            pane = QWidget()
            box = QVBoxLayout(pane)
            box.addWidget(QLabel(title))
            box.addWidget(address)
            box.addWidget(view)
            button = QPushButton("Atualizar / abrir caminho")
            button.clicked.connect(refresh)
            address.returnPressed.connect(refresh)
            box.addWidget(button)
            if view is self.remote:
                self.more = QPushButton("Carregar mais")
                self.more.setEnabled(False)
                self.more.clicked.connect(self.load_more)
                box.addWidget(self.more)
            split.addWidget(pane)
        self.local.itemDoubleClicked.connect(lambda item: self.open_directory(item, False))
        self.remote.itemDoubleClicked.connect(lambda item: self.open_directory(item, True))
        row = QHBoxLayout()
        outer.addLayout(row)
        for title, callback in [("Enviar →", self.upload), ("← Receber", self.download), ("Cancelar transferências", self.cancel)]:
            button = QPushButton(title)
            button.clicked.connect(callback)
            row.addWidget(button)
        self.progress = QLabel("Selecione arquivos e confirme o destino. Arquivos existentes são preservados.")
        outer.addWidget(self.progress)
        self.refresh_local()
        self.refresh_remote()

    @staticmethod
    def add(view, entries):
        for entry in entries:
            item = QListWidgetItem(("▸ " if entry["directory"] else "  ") + entry["name"])
            item.setData(Qt.ItemDataRole.UserRole, entry)
            view.addItem(item)

    def open_directory(self, item, remote):
        entry = item.data(Qt.ItemDataRole.UserRole)
        if entry["directory"]:
            (self.remote_address if remote else self.local_path).setText(entry["path"])
            (self.refresh_remote if remote else self.refresh_local)()

    def refresh_local(self):
        try:
            self.local.clear()
            self.add(self.local, listing(self.local_path.text()))
        except Exception as ex:
            self.progress.setText(str(ex))

    def refresh_remote(self):
        self.remote.clear()
        self.remote_path = self.remote_address.text()
        self.next_offset = 0
        self.load_more()

    def load_more(self):
        self.more.setEnabled(False)
        self.request = uuid.uuid4().hex
        self.owner.worker.call("send_file", {"kind":"browse", "transferId":self.request,
            "path":self.remote_path,"offset":self.next_offset})

    def listing(self, f):
        if f.get("transferId") == self.request:
            self.add(self.remote, f.get("entries") or [])
            offset = f.get("offset", 0)
            self.more.setEnabled(bool(f.get("more")) and isinstance(offset,int) and offset > self.next_offset)
            self.next_offset = offset
            if f.get("error"):
                self.progress.setText(f["error"])

    def selected(self, view):
        return [item.data(Qt.ItemDataRole.UserRole) for item in view.selectedItems()
                if not item.data(Qt.ItemDataRole.UserRole)["directory"]]

    def upload(self):
        entries = self.selected(self.local)
        if not self.remote_path or not entries:
            return
        if QMessageBox.question(self, "RemoteDesk", f"Enviar {len(entries)} arquivo(s) para {self.remote_path}?") != QMessageBox.StandardButton.Yes:
            return
        for entry in entries:
            sep = "/" if self.remote_path.startswith("/") else "\\"
            destination = self.remote_path.rstrip("/\\")+sep+entry["name"]
            self.owner.worker.call("file", "upload", entry["path"], destination)

    def download(self):
        entries = self.selected(self.remote)
        if not entries:
            return
        if QMessageBox.question(self, "RemoteDesk", f"Receber {len(entries)} arquivo(s) em {self.local_path.text()}?") != QMessageBox.StandardButton.Yes:
            return
        for entry in entries:
            self.owner.worker.call("file", "download", entry["path"], str(Path(self.local_path.text())/entry["name"]))

    def cancel(self):
        self.owner.worker.call("file", "cancel")


class Main(QMainWindow):
    def __init__(self, smoke=False):
        super().__init__()
        self.worker = self.viewer = self.indicator = self.files_window = None
        self.dialogs = []
        self.smoke = smoke
        self.setWindowTitle("RemoteDesk")
        self.resize(880, 580)
        area = QApplication.primaryScreen().availableGeometry()
        self.resize(min(880,area.width()-60), min(580,area.height()-60))
        self.root = Path(QStandardPaths.writableLocation(QStandardPaths.StandardLocation.AppLocalDataLocation))
        if not smoke:
            self.root.mkdir(parents=True, exist_ok=True)
            self.root.chmod(0o700)
        self.config = {}
        config_path = self.root/"settings.json"
        if not smoke and config_path.exists():
            self.config = json.loads(config_path.read_text())
        center = QWidget()
        box = QVBoxLayout(center)
        box.setContentsMargins(28, 24, 28, 24)
        self.setCentralWidget(center)
        title = QLabel("RemoteDesk")
        title.setStyleSheet("font-size:28px; font-weight:600")
        box.addWidget(title)
        box.addWidget(QLabel("Seu endereço"))
        self.number = QLabel("Aguardando conexão")
        self.number.setStyleSheet("font-size:32px; color:#438fff")
        box.addWidget(self.number)
        self.status = QLabel("Configure o mesmo servidor utilizado no cliente Windows.")
        self.status.setWordWrap(True)
        box.addWidget(self.status)
        self.server = QLineEdit(self.config.get("server", ""))
        self.server.setPlaceholderText("wss://servidor:5081/ws")
        self.relay = QLineEdit(self.config.get("relay", ""))
        self.relay.setPlaceholderText("Relay opcional: wss://domínio/relay/ws")
        self.sha = QLineEdit(self.config.get("sha256", ""))
        self.sha.setPlaceholderText("SHA-256 TLS para certificado LAN; vazio para HTTPS público")
        for label, field in [("Servidor", self.server),("Relay",self.relay),("SHA-256 TLS",self.sha)]:
            box.addWidget(QLabel(label))
            box.addWidget(field)
        save = QPushButton("Salvar e conectar ao servidor")
        save.clicked.connect(self.connect_server)
        box.addWidget(save)
        row = QHBoxLayout()
        self.target = QLineEdit()
        self.target.setPlaceholderText("ID do computador remoto")
        row.addWidget(self.target)
        self.connect_button = QPushButton("Conectar")
        self.connect_button.setEnabled(False)
        self.connect_button.clicked.connect(lambda: self.worker.call("request", self.target.text().replace(" ","")))
        row.addWidget(self.connect_button)
        box.addLayout(row)
        note = "Prévia multiplataforma • recebimento supervisionado • tela principal"
        if not host_supported():
            note += " • Wayland: somente visualizador e arquivos"
        box.addWidget(QLabel(note))
        box.addStretch()
        if self.config.get("server") and not smoke:
            from PySide6.QtCore import QTimer
            QTimer.singleShot(0, self.connect_server)

    def connect_server(self):
        if self.worker and self.worker.isRunning():
            self.status.setText("Feche e reabra o aplicativo para alterar o servidor.")
            return
        try:
            from .transport import tls_options
            self.config = {"server":self.server.text().strip(), "relay":self.relay.text().strip(),
                           "sha256":self.sha.text().strip().replace(":", "")}
            tls_options(self.config["server"], self.config["sha256"])
            key = identity(self.root)
            (self.root/"settings.json").write_text(json.dumps(self.config,indent=2))
            self.worker = Worker(self.config, key)
            self.worker.event.connect(self.on_event)
            self.worker.question.connect(self.question)
            self.worker.start()
            self.status.setText("Conectando…")
        except Exception as ex:
            self.status.setText(str(ex))

    def on_event(self, kind, value):
        if kind == "ready":
            self.number.setText(value)
            self.status.setText("Online • cada sessão recebida exige autorização.")
            self.connect_button.setEnabled(True)
        elif kind == "offline":
            self.connect_button.setEnabled(False)
            self.number.setText("Offline")
        elif kind in ("error", "status"):
            self.status.setText(value)
        elif kind == "frame" and self.viewer:
            self.viewer.screen.frame(value)
        elif kind == "started":
            if value.get("isHost"):
                indicator = QDialog(self, Qt.WindowType.Tool | Qt.WindowType.WindowStaysOnTopHint)
                indicator.setWindowTitle("RemoteDesk — Sessão ativa")
                row = QHBoxLayout(indicator)
                row.addWidget(QLabel("● Acesso remoto ativo — " + (value.get("name") or value["deviceId"])))
                end = QPushButton("✕ Encerrar")
                end.clicked.connect(lambda: self.worker.call("end"))
                row.addWidget(end)
                indicator.rejected.connect(lambda: self.worker.call("end"))
                indicator.show()
                self.indicator = indicator
            else:
                self.viewer = QMainWindow(self)
                self.viewer.setWindowTitle("RemoteDesk — " + value["deviceId"])
                self.viewer.resize(1100,720)
                widget = QWidget()
                layout = QVBoxLayout(widget)
                screen = Screen(lambda **d: self.worker.call("emit", "input", data=d))
                screen.control = bool(value.get("permissions",{}).get("control"))
                self.viewer.screen = screen
                layout.addWidget(screen,1)
                row = QHBoxLayout()
                layout.addLayout(row)
                row.addWidget(QLabel("Velocidade"))
                quality = QSlider(Qt.Orientation.Horizontal)
                quality.setRange(0,100)
                quality.setValue(60)
                quality.sliderReleased.connect(lambda: self.worker.call("emit", "quality", qualityLevel=quality.value()))
                row.addWidget(quality)
                row.addWidget(QLabel("Qualidade"))
                if value.get("permissions",{}).get("files"):
                    files = QPushButton("Arquivos")
                    files.clicked.connect(self.show_files)
                    row.addWidget(files)
                end = QPushButton("Encerrar")
                end.clicked.connect(lambda: self.worker.call("end"))
                row.addWidget(end)
                self.viewer.setCentralWidget(widget)
                self.viewer.closeEvent = lambda e: (self.worker.call("end"),e.accept())
                self.viewer.show()
        elif kind == "ended":
            for window in (self.viewer,self.indicator,self.files_window):
                if window:
                    window.hide()
                    window.deleteLater()
            self.viewer = self.indicator = self.files_window = None
            for dialog in list(self.dialogs):
                dialog.reject()
        elif kind == "listing" and self.files_window:
            self.files_window.listing(value)
        elif kind == "progress" and self.files_window:
            self.files_window.progress.setText(value)

    def show_files(self):
        if not self.files_window:
            self.files_window = Files(self)
        self.files_window.show()

    def question(self, token, kind, payload):
        if kind == "consent":
            dialog = QDialog(self)
            dialog.setWindowTitle("RemoteDesk — Autorizar sessão")
            box = QVBoxLayout(dialog)
            box.addWidget(QLabel(f"{payload.get('name') or ''} ({payload['deviceId']}) solicita acesso.\nPermitir visualizar a tela?"))
            control = QCheckBox("Permitir mouse e teclado")
            files = QCheckBox("Permitir navegar, enviar e receber arquivos")
            control.setEnabled(bool(payload.get("permissions",{}).get("control")))
            files.setEnabled(bool(payload.get("permissions",{}).get("files")))
            box.addWidget(control)
            box.addWidget(files)
            buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
            buttons.accepted.connect(dialog.accept)
            buttons.rejected.connect(dialog.reject)
            box.addWidget(buttons)
            result = lambda code: {"view":True,"control":control.isChecked(),"files":files.isChecked()} if code else None
        elif kind == "password":
            dialog = QInputDialog(self)
            dialog.setWindowTitle("RemoteDesk — Senha do destino")
            fingerprint = hashlib.sha256(base64.b64decode(payload["publicKey"],validate=True)).hexdigest().upper()
            dialog.setLabelText("Senha do dispositivo remoto\nChave pública SHA-256:\n"+fingerprint)
            dialog.setTextEchoMode(QLineEdit.EchoMode.Password)
            result = lambda code: dialog.textValue() if code else None
        else:
            dialog = QFileDialog(self, "RemoteDesk — Confirmar destino", str(Path.home()/payload["name"]))
            dialog.setAcceptMode(QFileDialog.AcceptMode.AcceptSave)
            result = lambda code: dialog.selectedFiles()[0] if code and dialog.selectedFiles() else None
        self.dialogs.append(dialog)
        def finished(code):
            self.worker.answer(token, result(code))
            if dialog in self.dialogs:
                self.dialogs.remove(dialog)
            dialog.deleteLater()
        dialog.finished.connect(finished)
        dialog.open()

    def closeEvent(self, event):
        if self.worker and self.worker.isRunning():
            self.worker.call("stop")
            if not self.worker.wait(15000):
                self.status.setText("Encerrando conexão; aguarde e feche novamente.")
                event.ignore()
                return
        event.accept()


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("RemoteDesk Portable")
    app.setOrganizationName("RemoteDesk")
    app.setStyleSheet(STYLE)
    assets = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[2]))/"assets/remotedesk-app-icon-256.png"
    if not assets.exists():
        assets = Path(__file__).resolve().parents[2]/"branding/final/icons/remotedesk-app-icon-256.png"
    app.setWindowIcon(QIcon(str(assets)))
    smoke = "--smoke-test" in sys.argv
    window = Main(smoke)
    window.show()
    if smoke:
        from PySide6.QtCore import QTimer
        QTimer.singleShot(300, app.quit)
    result = app.exec()
    # Destroy native widgets before QApplication and the frozen Python runtime unload Qt.
    window.close()
    window.deleteLater()
    QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    app.processEvents()
    return result
