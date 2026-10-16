#!/bin/sh
set -eu
source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
target="$HOME/.local/opt/RemoteDesk"
desktop="$HOME/.local/share/applications/remotedesk.desktop"
if [ -e "$target" ] && [ ! -f "$target/.remotedesk-owned" ]; then
  echo "Destino existente sem identificação RemoteDesk; instalação cancelada." >&2
  exit 1
fi
mkdir -p "$target" "$(dirname "$desktop")"
cp -a "$source_dir/." "$target/"
cat > "$desktop" <<EOF
[Desktop Entry]
Type=Application
Name=RemoteDesk
Comment=Acesso remoto autorizado
Exec="$target/RemoteDesk"
Icon=$target/RemoteDesk.png
Terminal=false
Categories=Network;RemoteAccess;
EOF
chmod 644 "$desktop"
printf 'Instalado em %s. Abra RemoteDesk no menu de aplicativos.\n' "$target"
