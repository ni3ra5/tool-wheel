<div align="center">

<img src="preview.png" alt="Tool Wheel" width="300">

# Tool Wheel

**A tactile radial launcher for macOS.**<br>
Hold a shortcut, and your favourite apps fan out around the cursor on a clicky dial.

[![Latest release](https://img.shields.io/github/v/release/ni3ra5/tool-wheel-mac?color=FF3C00&label=release)](https://github.com/ni3ra5/tool-wheel-mac/releases/latest)
![macOS 14+](https://img.shields.io/badge/macOS-14%2B-black?logo=apple)
![Apple silicon & Intel](https://img.shields.io/badge/Apple%20silicon%20%26%20Intel-universal-555)
![Swift](https://img.shields.io/badge/Swift-SwiftUI-F05138?logo=swift&logoColor=white)

[**⬇ Download the latest release**](https://github.com/ni3ra5/tool-wheel-mac/releases/latest)

</div>

---

## ✨ Features

- **Opens where you are** – hold **⌃⌥⌘** and the wheel appears around your cursor, on any screen or Space.
- **Feels physical** – molded-plastic slices, a knob that turns in clicky 10° detents with a soft, heavy *thock*, and haptics on Force Touch trackpads.
- **Two ways to open** – click a tool, or just hover and let go of the shortcut.
- **Shows what's running** – a small dot marks apps that are already open.
- **Your shortcut** – any combination of two or more of ⌃ ⌥ ⇧ ⌘.
- **No permissions needed** – no Accessibility or Input Monitoring prompts, no Dock icon.

## 📦 Install

> Requires macOS 14 or later.

1. Download `ToolWheel-<version>.zip` from the [latest release](https://github.com/ni3ra5/tool-wheel-mac/releases/latest) and unzip it.
2. Drag **Tool Wheel.app** into **Applications**.
3. Open it. The app isn't notarized by Apple, so macOS blocks the first launch: go to **System Settings → Privacy & Security**, scroll down and click **Open Anyway**. Or run:
   ```bash
   xattr -dr com.apple.quarantine "/Applications/Tool Wheel.app"
   ```
4. Settings opens on first launch. Open the app again any time to get back to it.

## 🎛 Using it

| Do this | To |
| --- | --- |
| Hold **⌃⌥⌘** | Open the wheel around the cursor |
| Hover a slice, then click (or release the keys) | Open that app |
| Click the ⚙︎ on the knob | Open Settings |
| Let go of the keys anywhere else | Close the wheel |

## ⚙️ Settings

- **Add apps** by searching everything installed; **remove** one with the trash icon on its slice.
- **Change the shortcut**, or restore the default with ↺.
- **Open apps by** clicking, or by releasing the shortcut.
- **Open at login.**

Everything is saved to `~/.config/toolwheel/tools.json`:

```json
{
  "wheel": [{ "name": "Safari", "path": "/Applications/Safari.app" }]
}
```

`path` is usually an app, but a file, folder or URL set by hand works too.

## 🛠 Build from source

```bash
swift run                      # run a debug build
scripts/build-app.sh 0.3.0     # make dist/Tool Wheel.app and a zip
```

To publish a release, push a version tag (`git tag v0.4.0 && git push origin v0.4.0`). GitHub Actions builds the universal app and attaches the zip.
