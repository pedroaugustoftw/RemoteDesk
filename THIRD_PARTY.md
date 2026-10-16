# Componentes de terceiros

O cliente Qt é distribuído em diretório, com bibliotecas dinâmicas substituíveis. Não há restrição à engenharia reversa necessária para depurar modificações dessas bibliotecas sob suas licenças. Os avisos/licenças das distribuições Python utilizadas são incluídos em `THIRD_PARTY_LICENSES` nos pacotes.

| Componente | Versão | Origem / licença |
|---|---|---|
| Qt for Python / PySide6 Essentials / Shiboken | 6.10.2 | https://code.qt.io/cgit/pyside/pyside-setup.git/ — LGPLv3, alternativa comercial |
| Qt Core, GUI, Widgets | 6.10.2 | https://code.qt.io/cgit/qt/qtbase.git/ — LGPLv3 |
| Python | 3.13 | https://www.python.org/downloads/source/ — PSF |
| Pillow | 12.3.0 | https://github.com/python-pillow/Pillow — HPND |
| cryptography | 50.0.1 | https://github.com/pyca/cryptography — Apache-2.0 ou BSD |
| websockets | 17.1 | https://github.com/python-websockets/websockets — BSD |
| keyring | 25.7.0 | https://github.com/jaraco/keyring — MIT |
| PyAutoGUI | 0.9.54 | https://github.com/asweigart/pyautogui — BSD |
| PyInstaller | 6.22.3 | https://github.com/pyinstaller/pyinstaller — GPL com exceção de distribuição do bootloader |

O código-fonte original RemoteDesk está neste repositório. Para reconstruir o cliente e trocar as bibliotecas Qt, veja `portable/requirements.txt`, `scripts/build-portable.py` e `.github/workflows/release.yml`. Nenhuma biblioteca Qt foi modificada. Os módulos Qt WebEngine/PDF não fazem parte do cliente.
