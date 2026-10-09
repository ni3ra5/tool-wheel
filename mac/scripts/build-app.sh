#!/bin/bash
# Builds "Tool Wheel.app" (universal: Apple silicon + Intel) and zips it into dist/.
# Usage: scripts/build-app.sh [version]
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:-0.0.0}"  # 0.0.0 = a local build, which never offers updates
APP="dist/Tool Wheel.app"

# One build per architecture, merged with lipo (works with just the Command Line Tools, no Xcode needed).
for arch in arm64 x86_64; do
  swift build -c release --triple "$arch-apple-macosx14.0" --scratch-path ".build/$arch"
done

rm -rf dist && mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp Resources/AppIcon.icns "$APP/Contents/Resources/"  # regenerate with scripts/make-icon.sh
lipo -create .build/arm64/release/ToolWheel .build/x86_64/release/ToolWheel -output "$APP/Contents/MacOS/ToolWheel"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Tool Wheel</string>
  <key>CFBundleDisplayName</key><string>Tool Wheel</string>
  <key>CFBundleIdentifier</key><string>com.ni3ra5.toolwheel</string>
  <key>CFBundleExecutable</key><string>ToolWheel</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# Ad-hoc signature: required to run on Apple silicon. Not notarized (needs a paid Apple Developer account).
codesign --force --sign - "$APP"

ditto -c -k --keepParent "$APP" "dist/ToolWheel-$VERSION.zip"
echo "Built dist/ToolWheel-$VERSION.zip"
