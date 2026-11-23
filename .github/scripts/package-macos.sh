#!/usr/bin/env bash
# Range une publication macOS dans LocalConvert.app, la signe « ad hoc » (sans compte développeur Apple)
# et en fait un zip qui conserve les droits d'exécution et les liens.
# Usage : package-macos.sh <dossier publié> <version> <zip de sortie>
set -euo pipefail
publish="$1"
version="$2"
zip="$3"

app="$(dirname "$zip")/LocalConvert.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish"/. "$app/Contents/MacOS/"
cp "$(dirname "$0")/../../assets/icon/localconvert.icns" "$app/Contents/Resources/localconvert.icns"
chmod +x "$app/Contents/MacOS/LocalConvert" "$app/Contents/MacOS/localconvert-cli"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>LocalConvert</string>
  <key>CFBundleDisplayName</key><string>LocalConvert</string>
  <key>CFBundleIdentifier</key><string>io.github.localconvert</string>
  <key>CFBundleExecutable</key><string>LocalConvert</string>
  <key>CFBundleIconFile</key><string>localconvert</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleDevelopmentRegion</key><string>fr</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
</dict>
</plist>
PLIST

# Signature ad hoc : obligatoire pour lancer du code natif sur les Mac Apple Silicon.
# Elle ne remplace pas une signature Apple : macOS affiche quand même un avertissement à la première ouverture.
# Sur Mac : codesign. Ailleurs (Linux) : rcodesign, installé par install-rcodesign.sh.
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$app"
  codesign --verify --deep --strict "$app"
else
  rcodesign sign "$app"
  for exe in LocalConvert localconvert-cli; do
    rcodesign verify "$app/Contents/MacOS/$exe"
  done
fi

rm -f "$zip"
if command -v ditto >/dev/null 2>&1; then
  ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
else
  # zip garde les droits d'exécution et, avec -y, les liens symboliques.
  zip_abs="$(cd "$(dirname "$zip")" && pwd)/$(basename "$zip")"
  (cd "$(dirname "$app")" && zip -qry "$zip_abs" "$(basename "$app")")
fi
ls -l "$zip"
