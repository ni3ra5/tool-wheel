#!/bin/bash
# Regenerates Resources/AppIcon.icns from the AppIcon view in main.swift. Run after changing the icon's look.
set -euo pipefail
cd "$(dirname "$0")/.."

swift build
TMP=$(mktemp -d)
.build/debug/ToolWheel --icon "$TMP/icon.png"

SET="$TMP/AppIcon.iconset"
mkdir "$SET"
for size in 16 32 128 256 512; do
  sips -z $size $size "$TMP/icon.png" --out "$SET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$TMP/icon.png" --out "$SET/icon_${size}x${size}@2x.png" >/dev/null
done
mkdir -p Resources
iconutil -c icns "$SET" -o Resources/AppIcon.icns
rm -rf "$TMP"
echo "Wrote Resources/AppIcon.icns"
