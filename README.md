# Tool Wheel

A radial launcher for macOS. Hold **⌃⌥⌘** (changeable in Settings) and a wheel of tools opens around your cursor; hover a tool and click to open it. Release the keys to dismiss.

![Tool Wheel](preview.png)

## Install

Requires macOS 14 or later (Apple silicon or Intel).

1. Download `ToolWheel-<version>.zip` from the [latest release](https://github.com/ni3ra5/tool-wheel-mac/releases/latest) and unzip it.
2. Drag **Tool Wheel.app** into **Applications**.
3. Open it. The app isn't notarized by Apple, so the first launch is blocked: go to **System Settings → Privacy & Security**, scroll down and click **Open Anyway**. Or, in Terminal:
   ```bash
   xattr -dr com.apple.quarantine "/Applications/Tool Wheel.app"
   ```
4. Settings opens on first launch. Hold **⌃⌥⌘** anywhere to bring up the wheel. Open the app again any time to get back to Settings.

## Build from source

```bash
swift run                      # run a debug build
scripts/build-app.sh 0.1.0     # make dist/Tool Wheel.app and a zip
```

To publish a release, push a version tag (`git tag v0.2.0 && git push origin v0.2.0`); GitHub Actions builds the app and attaches the zip.

## Settings

Open the wheel and click the gear in the centre. From there you can:

- add apps by searching your installed apps, and remove one with the trash icon on its slice
- change the shortcut (any two or more of ⌃ ⌥ ⇧ ⌘), or restore the default
- choose how apps open: click them, or hover and release the shortcut
- turn on launching at login

Everything is saved to `~/.config/toolwheel/tools.json`:

```json
{
  "wheel": [{ "name": "Safari", "path": "/Applications/Safari.app" }]
}
```

`path` is usually an app, but a file, folder or URL set by hand works too.
