#!/usr/bin/env sh
# Adds LocalConvert to the applications menu (with its icon) for the current user.
# Run it from the LocalConvert folder: ./install-desktop-entry.sh   (run it again if you move the folder)
set -e
dir="$(cd "$(dirname "$0")" && pwd)"
apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$apps"
cat > "$apps/localconvert.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=LocalConvert
Comment=Offline PDF, image, video and audio converter
Comment[fr]=Convertisseur PDF, images, vidéos et sons hors ligne
Comment[de]=Offline-Konverter für PDFs, Bilder, Videos und Audio
Comment[es]=Conversor de PDF, imágenes, vídeos y audio sin conexión
Exec="$dir/LocalConvert" %F
Icon=$dir/localconvert.png
Terminal=false
Categories=Utility;Office;AudioVideo;Graphics;
StartupWMClass=LocalConvert
DESKTOP
chmod +x "$apps/localconvert.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps" >/dev/null 2>&1 || true
echo "LocalConvert added to the applications menu ($apps/localconvert.desktop)."
