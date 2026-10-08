#!/bin/bash
# Renders the app icon from the AppIcon view in main.swift and writes every format the repo uses:
#   mac/Resources/AppIcon.icns, windows/AppIcon.ico, assets/icon.png (README).
# Run after changing the icon's look.
set -euo pipefail
cd "$(dirname "$0")/.."   # mac/

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

sips -z 256 256 "$TMP/icon.png" --out ../assets/icon.png >/dev/null

# .ico: a directory of PNGs (supported since Windows Vista).
ICO_SIZES="16 24 32 48 64 128 256"
for size in $ICO_SIZES; do sips -z $size $size "$TMP/icon.png" --out "$TMP/ico_$size.png" >/dev/null; done
python3 - "$TMP" ../windows/AppIcon.ico $ICO_SIZES <<'PY'
import struct, sys
tmp, out, sizes = sys.argv[1], sys.argv[2], [int(s) for s in sys.argv[3:]]
pngs = [open(f"{tmp}/ico_{s}.png", "rb").read() for s in sizes]
offset = 6 + 16 * len(sizes)
entries = b""
for s, png in zip(sizes, pngs):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(png), offset)
    offset += len(png)
open(out, "wb").write(struct.pack("<HHH", 0, 1, len(sizes)) + entries + b"".join(pngs))
PY

rm -rf "$TMP"
echo "Wrote mac/Resources/AppIcon.icns, windows/AppIcon.ico, assets/icon.png"
