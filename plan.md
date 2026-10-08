# Tool Wheel – plan

A radial launcher: hold a shortcut and a wheel of apps opens around the cursor; hover one and click (or release) to open it. Native on macOS and Windows, with a tactile, physical look.

Repo: <https://github.com/ni3ra5/tool-wheel> (public).

## Status

| Platform | State | Latest release |
| --- | --- | --- |
| macOS | Working, used daily. Settings complete. | v0.3.0 (wheel, settings, icon). Reorder/glide and centred Settings are on `main`, not released yet. |
| Windows | Preview. Compiles in CI; **never run on a real PC yet**. No Settings window. | None yet (CI artifact only). |

## Architecture

```
mac/        Swift / SwiftUI + AppKit, Swift Package (no Xcode project)
  Sources/ToolWheel/main.swift      config, wheel view, knob, app delegate, --snapshot / --icon modes
  Sources/ToolWheel/Settings.swift  settings window, wheel editor, shortcut recorder, login item
  Sources/ToolWheel/Click.swift     synthesised detent sound
  scripts/build-app.sh              universal .app + zip
  scripts/make-icon.sh              icon for every platform
windows/    C# / WPF, .NET 8
  Program.cs     entry, tray, 60Hz loop, open/click/release, login item
  WheelWindow.cs drawing (same numbers as the Mac)
  Config.cs      tools.json
  Native.cs      Win32: key state, cursor/monitors, mask key, shell icons
  Click.cs       detent sound
assets/     icon.png, preview.png (README)
.github/workflows/  release-mac.yml (mac-v* tags), windows.yml (every windows/ change; releases on windows-v* tags)
```

Shared on both platforms: the same geometry numbers, accent `#FF3C00`, the same `tools.json` shape (`wheel`, `shortcut`, `releaseToOpen`), and the same sound recipe.

## Decision log

Newest at the bottom. Each entry: what was decided, and why.

### 2026-10-08

- **Native Mac app as a Swift Package**, not an Xcode project. Smallest setup that builds with just the Command Line Tools.
- **Shortcut = holding modifier keys, polled 60×/s.** Needs no Accessibility or Input Monitoring permission. Consequence: shortcuts are modifier-only, at least two keys (one key would fire while typing). Default ⌃⌥⌘.
- **Config in `~/.config/toolwheel/tools.json`, re-read every time the wheel opens.** Edits apply without a restart. An unreadable file is moved to `tools.json.invalid` instead of being overwritten.
- **Visual direction: tactile, physical hardware** (references: a field recorder, Yodiz toggle switches, an amp knob). Light molded plastic, light from the top, subtle textures, small real shadows.
- **Wheel = pie slices filling the ring**, not floating buttons. Click areas get wider toward the edge. Gaps between slices are a constant width (edges parallel to the radial line), corners rounded.
- **Frosted blur behind the wheel** (`NSVisualEffectView`, behind-window). Its rim extends exactly one gap past the wheel; its border is bright at the top and fades toward the bottom.
- **Centre = a dial knob** (outer ring, face, engraved tick scale) that casts one shadow straight down. All shadows are offset at least as far as they blur, so nothing shows above the knob.
- **Knob turns in 10° detents** toward the cursor, with a synthesised low "thock" and trackpad haptic, at most one click per 50 ms at volume 0.15. Rotation is stepped, not fluid.
- **Selection indicator = a short solid accent line** on the knob rim, with a soft glow, turning with the knob. Shown only while a tool is hovered. (Replaced a dot, then a long glowing rim bar.)
- **Accent colour `#FF3C00`** everywhere: pointer, gear hover, settings tint.
- **Hover counts only on the slice ring** (between knob and outer edge). Outside the wheel or over the knob nothing is active.
- **Gaps:** 2 px between slices, 1.5 px between slices and knob.
- **Tool name on the knob**, two lines then "…". Settings gear below it.
- **Running apps get a subtle grey dot** between the icon and the knob.
- **App icons get a hairline outline**, and no added shadow.
- **Settings window: dark, minimal, flat**, dotted background, no shadows. Opens centred on the screen with the cursor.
  - Left: the wheel as it looks (static, not usable).
  - Right: search of installed apps; click a row to add it.
  - Bottom-left: open at login. Bottom-right: shortcut (restore button left of the keys), then "Open apps by" (Click / Release).
- **Removed custom tools** (shell commands, custom icons). The wheel holds apps; files, folders and URLs only by editing JSON.
- **Two open modes:** click a tool, or hover and release the shortcut.
- **Open at login via a LaunchAgent plist**, because a bare executable can't use `SMAppService`. Kept in the `.app` for now.
- **Distribution:** universal `.app` (two builds merged with `lipo`), ad-hoc signed, **not notarized** (needs a paid Apple Developer account). Built and attached to GitHub Releases by Actions on version tags.
- **App icon is rendered from the knob view itself**, so it always matches.
- **Repo made public.** Commits use khan.nibras28@gmail.com, pushed through the `ni3ra5` GitHub account.

### 2026-10-09

- **Reordering in Settings via controls outside the wheel:** each slice has a pill (trash + six-dot grip) just past the rim. Drag only by the grip, with a hand cursor. Pills keep equal clearance from the rim at every angle.
- **Reorders animate physically:** icons are keyed to their app and glide along the arc the short way round.
- **One repo for both platforms**, renamed `tool-wheel-mac` → `tool-wheel`: `mac/`, `windows/`, shared `assets/`. Release tags `mac-v*` and `windows-v*`.
- **Windows app in C# / WPF on .NET 8**, shipped as a self-contained single `.exe` (x64 and ARM64) so nothing needs installing.
  - Same approach as the Mac: poll held modifiers, no keyboard hook. Default **Ctrl+Alt+Win**.
  - Taps an unassigned key when the wheel opens so releasing Win/Alt doesn't open Start or a menu bar.
  - Lives in the tray (edit tools, open at login via the `HKCU\…\Run` key, quit).
  - Config in `%APPDATA%\ToolWheel\tools.json`; Settings is "open the JSON in Notepad" until the window is ported.
  - No live blur yet (WPF transparent windows can't host acrylic); a grey disc stands in.
- **Windows is compile-checked in GitHub Actions only**, since there's no Windows machine here.
- **Keep `plan.md` as the running record**, with every decision logged here (see `CLAUDE.md`).

## Next

**Windows**
- Run it on a real PC; likely fixes: DPI/cursor maths across monitors, icon extraction.
- Port the Settings window (app search, grip reordering, shortcut recorder, open mode).
- Frosted backdrop (likely needs a WinUI/DWM approach), "open" dots for `.lnk` tools.
- First release: tag `windows-v0.1.0`.

**macOS**
- Release `mac-v0.4.0` with reordering, gliding icons and the centred Settings window.
- Offer to move the app into Applications on first launch (avoids App Translocation).
- Notarization, if an Apple Developer account is set up.
- Switch login item to `SMAppService` once properly signed.

## Known limits

- Shortcuts are modifier-only (no letter keys) on both platforms; letter keys would need permissions or a hook.
- Mac downloads show a Gatekeeper warning until notarized; Windows shows SmartScreen until signed.
- The Settings preview can't show the live blur (a grey disc stands in).

## Working notes

- Check Mac visuals without the hotkey: `mac/.build/debug/ToolWheel --snapshot out.png` (skips the blur).
- Re-render icons after changing the knob: `mac/scripts/make-icon.sh`.
