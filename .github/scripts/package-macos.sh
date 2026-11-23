#!/usr/bin/env bash
# Range une publication macOS dans AllInOne.app, la signe « ad hoc » (sans compte développeur Apple)
# et en fait un zip qui conserve les droits d'exécution et les liens.
# Usage : package-macos.sh <dossier publié> <version> <zip de sortie>
set -euo pipefail
publish="$1"
version="$2"
zip="$3"

app="$(dirname "$zip")/AllInOne.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish"/. "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/AllInOne" "$app/Contents/MacOS/allinone-cli"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>AllInOne</string>
  <key>CFBundleDisplayName</key><string>AllInOne</string>
  <key>CFBundleIdentifier</key><string>io.github.allinone</string>
  <key>CFBundleExecutable</key><string>AllInOne</string>
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
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

rm -f "$zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
ls -l "$zip"
