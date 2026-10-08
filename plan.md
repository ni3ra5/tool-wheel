# Tool Wheel – plan

A radial launcher: hold a shortcut and a wheel of apps opens around the cursor; hover one and click (or release) to open it. Native on macOS and Windows, with a tactile, physical look.

Repo: <https://github.com/ni3ra5/tool-wheel> (public).

## Status

| Platform | State | Latest release |
| --- | --- | --- |
| macOS | Working, used daily. Settings complete. | v0.4.0 (reordering, gliding icons, centred Settings, add/remove animation, smaller backdrop). |
| Windows | Preview, being tested on a real PC (Windows 11). Wheel, Settings window and focus handling work. | v0.1.2 (add/remove animation, smaller backdrop). Portable `.exe`, no installer. |
| Website | Landing page in `site/` with download buttons and a playable wheel demo. Not deployed yet. | – |

## Architecture

```
mac/        Swift / SwiftUI + AppKit, Swift Package (no Xcode project)
  Sources/ToolWheel/main.swift      config, wheel view, knob, app delegate, --snapshot / --icon modes
  Sources/ToolWheel/Settings.swift  settings window, wheel editor, shortcut recorder, login item
  Sources/ToolWheel/Click.swift     synthesised detent sound
  scripts/build-app.sh              universal .app + zip
  scripts/make-icon.sh              icon for every platform
windows/    C# / WPF, .NET 8
  Program.cs         entry, tray, 60Hz loop, open/click/release, login item, --snapshot modes
  WheelWindow.cs     WheelView (drawing, same numbers as the Mac; edit mode for Settings) and the floating window
  SettingsWindow.cs  settings window, wheel editor, app list, shortcut recorder
  Apps.cs            open-or-bring-forward, installed apps (Start menu's All apps)
  Config.cs          tools.json
  Native.cs          Win32: key state, cursor/monitors, mask key, other apps' windows, focus, shell icons
  Click.cs       detent sound
assets/     icon.png, preview.png (README)
site/       landing page for Vercel: plain index.html, style.css, script.js (+ copies of icon.png, preview.png); vercel.json at the root points Vercel here
.github/workflows/  release-mac.yml (mac-v* tags), mac.yml (compile check on every mac/ change), windows.yml (every windows/ change; releases on windows-v* tags)
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
- **Windows dev machine set up** (Windows 11, .NET 8 SDK): the app is now built and run locally, not only compile-checked in CI. First fix: `Tool.IsRunning` was being saved into `tools.json`; it's now `[JsonIgnore]`.
- **Opening a tool that's already open brings its window forward** (restored if minimised, to maximised if it was) instead of starting another copy, like the Mac. Before, File Explorer opened a new window every time. Windows are matched to tools by `.exe`, or by app ID for Store apps; a folder tool reuses an Explorer window already showing it.
- **The wheel hands focus to what it opens.** Windows only lets the front app give focus away and the wheel never takes focus, so new apps opened behind (taskbar button flashing). It now briefly joins the front app's input queue to allow it, then watches up to 5 s for the new window and raises it.
- **Settings window ported from the Mac** (replaced "open tools.json in Notepad"): same layout, dark, flat, dotted, dark title bar in the background colour. Opens from the wheel's gear or the tray (double-click or "Settings…"); the tray's own "Open at login" item moved into it.
  - App list = the Start menu's All apps, minus uninstallers, help files and web links. Desktop apps are saved by `.exe` path, Store apps (and shell places like Control Panel) as `shell:AppsFolder\<app ID>`.
  - Shortcut recorder polls held keys like the wheel (WPF doesn't reliably see Win) and taps the mask key so recording Win doesn't open Start.
- **The Settings app list also includes apps pinned to the taskbar or on the desktop**, because an app whose Start menu shortcut is missing (Chrome on the test PC) isn't in All apps. **Start entries with arguments** (Chrome web apps, Git Bash, mmc consoles) **are saved by app ID**, not as the bare `.exe`, which opened the wrong thing; these may start another copy instead of reusing a window.
- **Admin windows in front block the shortcut; left as a known limit** rather than running Tool Wheel as admin. Chosen by the user over an optional run-as-admin setting and a signed `uiAccess` build.
- **First Windows release `windows-v0.1.0`: portable self-contained `.exe`**, unsigned, no installer. Shareable now that the wheel works on a real PC; an installer and signing can come later.
- **Windows click volume 0.035, down from 0.15** (the user found it too loud, then still too loud at 0.07). The Mac stays at 0.15, a deliberate gap in the shared sound recipe.
- **Windows click plays through one WASAPI stream (NAudio) held open while the wheel is up**, like the Mac's AVAudioEngine; replaced `SoundPlayer`. `SoundPlayer` opened a stream per click and an idle device ate the start of the 45 ms sound, so slow turns crackled and broke (confirmed with a loopback recording; now every click matches the intended sound). The stream reopens on every wheel open, so a changed default device is picked up. NAudio.Wasapi is the Windows app's first package dependency.
- **Adding or removing a tool in Settings animates, on both Mac and Windows**: the slice opens in the gap (or closes where it was) while the other slices narrow or widen and glide round, and its icon and pill grow and fade in (or shrink and fade out, the icon quickly and under the neighbour sliding in). Slices are now keyed to their tool and glide on reorder too. Timings match: 350 ms spring for slices, 250 ms fade-in, 180 ms icon fade-out, 350 ms pill fade-out. Windows: one animated angle/span/lift (`Polar`) per tool. Mac: animatable `Slice` view plus `Opening`/`popping` transitions. The Mac side is compile-checked only (no Mac here); check the motion and smoothness on a Mac.
- **The UI and animations are always the same on Mac and Windows** (user's rule, now in `CLAUDE.md`). Visual and motion changes go into both apps in the same change; only platform plumbing may differ.
- **Mac compile check in GitHub Actions** (`mac.yml`, every push touching `mac/`), since Mac code is now also edited from the Windows machine.
- **Default Notepad, Calculator and Settings are their Store apps** (`shell:AppsFolder\…`) when installed. On Windows 11 the old `notepad.exe`/`calc.exe` only hand off to them, so their windows couldn't be matched.
- **Icons via the shell's `IShellItemImageFactory`**, which covers files, folders and Store apps; cached per session.
- **`--snapshot out.png` / `--snapshot-settings out.png`** render the wheel or Settings to a PNG, for checking visuals without the shortcut (like the Mac's `--snapshot`).
- **Landing page in `site/`, plain HTML/CSS/JS with no build step**, hosted on Vercel (project Root Directory = `site`). Small enough not to need a framework.
  - **Download buttons fetch the newest release from the GitHub API** and link its zip directly (Mac: newest `mac-v*` or the older plain `v*` tags; Windows: `win-x64`). On failure they fall back to the Releases page. The visitor's OS gets an accent "LED" and goes first.
  - **Playable demo styled like the Settings window**: dark dotted panel, the wheel with trash/grip pills, app list on the right. Hover turns the knob in detents and lifts the slice; click "opens" (adds the running dot); drag a slice or grip to reorder (others glide); trash or the list removes; the list adds (up to 12). On touch, press the knob and slide out, release to open.
  - **Same geometry, accent and easing as the apps**, in a 520-unit SVG viewBox (room for the pills). The site's frosted disc uses a real `backdrop-filter`.
  - **Generic, self-drawn app icons** (Browser, Mail, Files…) instead of real brand icons.
  - Typeface Geist / Geist Mono; page follows the system light/dark theme, the demo panel stays dark like Settings.
- **Website hero trimmed to icon, name, one line and the two buttons** (user: too much text). Dropped the shortcut keycaps line and the fine print (ARM64 link, unsigned warning); "Install notes" moved to the footer.
- **Download buttons are equal width (210 px) and just say "Mac" / "Windows" with "Download vX.Y.Z"**. On hover they rise with a deeper skirt, their LED lights and the arrow turns accent; pressed, they sink.
- **No dot pattern on the demo panel**; it's plain `#181818`. The page background keeps its dots.
- **No sound in the website demo** (replaced the opt-in "Sound on" toggle and the synthesised thock). Detents are silent; phones that support it get a 4 ms vibration per detent.
- **Backdrop stops inside the wheel** (8 px short of the rim) instead of extending one gap past it. It still fills the gaps between slices but never shows outside the edge. Its lit border went with it (no rim left to light). Replaces the 2026-10-08 "rim one gap past the wheel" decision. Mac, Windows and the website demo.
- **Every Mac or Windows release must show up on the landing page's download buttons.** The site already picks up the newest release from the GitHub API, so the rule is to keep release tags and zip names in the shape it matches (`mac-v*` + `.zip`, `windows-v*` + `*-win-x64.zip`), update `site/` in the same change when that shape or the download set changes, and check both buttons after publishing. Written into `CLAUDE.md` under Conventions.
- **Root `vercel.json` sets the output directory to `site/`** (no install or build). The first Vercel deploy served the repo root and returned 404 because there's no `index.html` there; the file makes it work without relying on the dashboard's Root Directory setting.

## Next

**Windows**
- Finish testing on a real PC; likely fixes: DPI/cursor maths across monitors, icon extraction.
- Frosted backdrop (likely needs a WinUI/DWM approach).
- `.lnk` tools (hand-edited JSON only) always start a new copy and get no "open" dot; resolve the shortcut target to match windows.
- Installer (Inno Setup or MSIX) for a Start menu entry and uninstaller; code signing to drop the SmartScreen warning.

**Website**
- Vercel project exists; get it serving `site/` (root `vercel.json`, or Root Directory `site` in the dashboard), then add the URL to the README.

**macOS**
- Offer to move the app into Applications on first launch (avoids App Translocation).
- Notarization, if an Apple Developer account is set up.
- Switch login item to `SMAppService` once properly signed.

## Known limits

- Shortcuts are modifier-only (no letter keys) on both platforms; letter keys would need permissions or a hook.
- Mac downloads show a Gatekeeper warning until notarized; Windows shows SmartScreen until signed.
- The Settings preview can't show the live blur (a grey disc stands in).
- Windows: the shortcut doesn't work while an admin window is in front (Task Manager, which always runs as admin for admin accounts; admin terminals; installers). Windows hides key state from normal apps then, so the 60Hz poll sees nothing and the Start-menu mask key is blocked too. Hooks are blocked the same way. Fixes, if it's ever wanted: an optional run-as-admin mode (scheduled task at login, apps launched unelevated via Explorer), or `uiAccess` (needs a signed exe installed in Program Files).
- Windows: apps open on another virtual desktop count as not open, so a new copy starts on the current one.

## Working notes

- Check Mac visuals without the hotkey: `mac/.build/debug/ToolWheel --snapshot out.png` (skips the blur).
- Re-render icons after changing the knob: `mac/scripts/make-icon.sh`.
- Windows: `cd windows && dotnet build`, then run `bin/Debug/net8.0-windows/ToolWheel.exe` (quit from the tray icon before rebuilding). Config is `%APPDATA%\ToolWheel\tools.json`; delete it to get the defaults back.
- Website locally: `python -m http.server 4173 --directory site` (or the `site` entry in `.claude/launch.json`).
- Windows visuals without the shortcut: `bin/Debug/net8.0-windows/ToolWheel.exe --snapshot out.png` (or `--snapshot-settings`).
