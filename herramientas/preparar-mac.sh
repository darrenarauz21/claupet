#!/bin/sh
# Prepara Claupet en macOS: la copia a Aplicaciones, le da permiso de ejecución, la firma
# solo para este equipo (sin cuenta de Apple) y le quita la cuarentena. Se ejecuta una vez:
#     sh preparar-mac.sh
set -e
cd "$(dirname "$0")"

APP="Claupet.app"
DEST="/Applications"
[ -w "$DEST" ] || DEST="$HOME/Applications"
mkdir -p "$DEST"

# Hasta la 2.1 se llamaba «Uso de Claude.app»: se cierra y se quita para no dejar dos copias.
pkill -f "/Contents/MacOS/UsoDeClaude" 2>/dev/null || true
rm -rf "$DEST/$APP" "$DEST/Uso de Claude.app"
cp -R "$APP" "$DEST/"
chmod +x "$DEST/$APP/Contents/MacOS/UsoDeClaude"
xattr -dr com.apple.quarantine "$DEST/$APP" 2>/dev/null || true
codesign --force --deep --sign - "$DEST/$APP"

echo "Claupet quedó en $DEST. La abro…"
open "$DEST/$APP"
echo "Búsquela en la barra de menús, arriba a la derecha."
