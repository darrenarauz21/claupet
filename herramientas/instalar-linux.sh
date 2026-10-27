#!/bin/sh
# Instala Claupet para este usuario (sin sudo): la app en ~/.local/share/claupet,
# el ícono y la entrada del menú de aplicaciones. Para actualizar, se vuelve a ejecutar.
set -e
cd "$(dirname "$0")"

DEST="$HOME/.local/share/claupet"
APPS="$HOME/.local/share/applications"
ICONS="$HOME/.local/share/icons/hicolor/256x256/apps"
mkdir -p "$DEST" "$APPS" "$ICONS"

# Las versiones anteriores se instalaban como «uso-de-claude»: se cierra y se quita para no dejar dos copias.
pkill -f "$HOME/.local/share/uso-de-claude/UsoDeClaude" 2>/dev/null || true
rm -rf "$HOME/.local/share/uso-de-claude" "$APPS/uso-de-claude.desktop" "$ICONS/uso-de-claude.png"

pkill -f "$DEST/Claupet" 2>/dev/null || true
cp Claupet "$DEST/Claupet"
chmod +x "$DEST/Claupet"
cp claupet.png "$ICONS/claupet.png"

cat > "$APPS/claupet.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Claupet
Comment=Mascota de escritorio, sesiones de Claude Code y avisos
Exec="$DEST/Claupet"
Icon=claupet
Terminal=false
Categories=Utility;
EOF

echo "Claupet quedó instalado en $DEST"

# Bibliotecas que la ventana necesita (vienen en los escritorios habituales).
MISSING=""
for lib in libICE.so.6 libSM.so.6 libfontconfig.so.1; do
  ldconfig -p 2>/dev/null | grep -q "$lib" || MISSING="$MISSING $lib"
done
if [ -n "$MISSING" ]; then
  echo
  echo "Faltan bibliotecas del sistema:$MISSING"
  echo "  Ubuntu, Debian, Mint:  sudo apt install libice6 libsm6 libfontconfig1"
  echo "  Fedora:                sudo dnf install libICE libSM fontconfig"
  echo "  Arch:                  sudo pacman -S libice libsm fontconfig"
fi

if ! command -v paplay >/dev/null 2>&1 && ! command -v pw-play >/dev/null 2>&1 && ! command -v aplay >/dev/null 2>&1; then
  echo
  echo "Para oír los avisos instale un reproductor, por ejemplo: sudo apt install pulseaudio-utils"
fi

if [ "${XDG_CURRENT_DESKTOP#*GNOME}" != "$XDG_CURRENT_DESKTOP" ]; then
  echo
  echo "En GNOME, el ícono de la bandeja necesita la extensión «AppIndicator and KStatusNotifierItem Support»"
  echo "(en Ubuntu viene activada; en otras: sudo apt install gnome-shell-extension-appindicator)."
fi

if [ -z "$MISSING" ]; then
  nohup "$DEST/Claupet" >/dev/null 2>&1 &
  echo
  echo "La app ya está abierta: busque el ícono en la bandeja. También está en el menú de aplicaciones."
fi
