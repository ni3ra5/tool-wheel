# Tool Wheel – plan

A radial launcher: hold a shortcut and a wheel of apps opens around the cursor; hover one and click (or release) to open it. Native on macOS and Windows, with a tactile, physical look.

Repo: <https://github.com/ni3ra5/tool-wheel> (public).

## Status

| Platform | State | Latest release |
| --- | --- | --- |
| macOS | Working, used daily. Settings complete. | v0.4.0 (reordering, gliding icons, centred Settings, add/remove animation, smaller backdrop). |
| Windows | Preview, being tested on a real PC (Windows 11). Wheel, Settings window and focus handling work. | v0.1.3 (slot colours, hold-to-delete, open at login via a Startup shortcut). Portable `.exe`, no installer. |
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
- **Site download buttons sort releases by publish date** before picking the newest. The GitHub API listed the old plain `v0.x` Mac releases ahead of `mac-v0.4.0`, so the Mac button kept offering v0.3.0.
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
- **Slots can be colour-coded** with a band along the slice's outer edge (4 px, following its rounded corners, lifting with it). **The colour belongs to the app, not the position**: stored per tool as hex (`"color": "#34C759"` in `tools.json`), so it moves with the app when reordered and can group related apps. Picked in Settings from a colour dot in each slice's pill (now 80 px wide: colour, trash, grip), which opens a swatch grid: None plus 8 presets (Orange `#FF3C00`, Amber `#FFB300`, Green `#34C759`, Teal `#30B0C7`, Blue `#0A84FF`, Purple `#AF52DE`, Pink `#FF2D55`, Graphite `#8E8E93`). **Mac only for now**; Windows and the website demo are listed under Next.
- **Removing a slot in Settings is press-and-hold on its trash**, not a click: the pill fills with accent from left to right over 0.7 s, then the tool is removed (with a trackpad tap). Letting go early drains the fill and nothing happens. Prevents accidental deletes. Mac only for now (Windows still deletes on click; see Next).
- **Slot colours and press-and-hold delete ported to Windows**, so both apps match again (the "UI is the same on Mac and Windows" rule in `CLAUDE.md`, which the two entries above broke). Same presets, 4 px band, 80 px pill and 0.7 s fill; Windows cancels the hold if the pointer wanders 30 px, like the Mac.
- **Themes, picked from a dropdown at the top right of the Settings wheel panel.** Two for now:
  - **Porcelain**: the original white plastic (the default).
  - **Graphite**, after the Huly Dial reference: dark graphite slices with a faint grille, a diagonal hatch over the hovered slice, a bright silver knob with a knurled rim, a warm accent glow round the knob's edge on the side it points to, a dark well and backdrop, and a faint ring just outside the wheel.
  - A theme only changes colours and surface details; geometry, timings and the accent stay shared. Stored as `"theme"` in `tools.json`. Same names and values on Mac (`Theme`/`Finish` in `main.swift`) and Windows (`Finish` in `WheelWindow.cs`).
  - **Switching is instant** on both (WPF can't crossfade the brushes the way SwiftUI does, so the Mac doesn't either). The Settings window itself stays dark.
  - The Mac's `--snapshot out.png --graphite` previews Graphite without changing the saved theme.
- **Themes removed** (the user's call), along with the Theme dropdown and the `theme` setting: the wheel has one look again, the original white Porcelain. Replaces the themes entry above. Slot colours and press-and-hold delete stay, on both platforms.
- **Website feature headings rewritten to say what the app does**, because the old ones read as innuendo together ("Small, quick, and nice to touch", "Feels physical", "Click, or just let go"). Now: "Your apps, one shortcut away"; cards "Appears at your cursor", "Designed like hardware", "Two ways to launch", "Shows running apps", "No permissions needed", "Easy to customise". Body text unchanged.
- **Windows "Open at login" is a `Tool Wheel.lnk` shortcut in the user's Startup folder**, replacing the `HKCU\…\Run` entry. On the test PC Windows skipped the Run entry at sign-in with no error (the Shell-Core log shows every other Run app starting) and Task Manager's Startup apps never listed it, even after re-creating the value; no policy, StartupApproved flag or security block explained it. A Startup shortcut to the same exe appeared in Task Manager at once and started the app after a restart. On launch the app moves any old Run entry over to the shortcut.
- **The shortcut can include a mouse side button** (button 4 or 5), on its own or with modifier keys (e.g. `Mouse 4`, `Ctrl + Mouse 4`); the user's call over "button alone only" and "button only with keys". Stored as `"mouseButton": 4` next to `shortcut` in `tools.json` (shortcut `0` = no keys); no keys and no button falls back to the default. Recorded in Settings like keys: press a side button, alone or while holding keys, and let go. Keyboard-only shortcuts still need two or more keys. Mac and Windows both.
  - **While a side button is the shortcut, its normal Back/Forward is blocked** (the user's call over letting it through). Only the exact combination is swallowed; the same button with other keys still goes Back. The keys must already be held when the button goes down.
  - Windows: a low-level mouse hook (`WH_MOUSE_LL`), no permission needed, installed the first time a side button is the shortcut, on its own thread so a busy UI thread can't stall the system's mouse. Replaces "no hooks at all" on Windows for this case only; keys are still polled.
  - Mac: a `CGEventTap`, which needs **Accessibility permission**, asked for only when a side button is recorded. Without it the wheel still opens (polling `pressedMouseButtons`) but the button also goes Back/Forward. README and the site's "No permissions needed" card say so.

### 2026-10-10

- **Optional on/off shortcut that turns the wheel off and back on**, set in Settings; none by default. Mac and Windows both. The user's calls:
  - **Modifier keys plus one regular key** (e.g. `Ctrl + Alt + P`, `⌃⌥P`), at least one modifier, rather than modifiers only (which would fire during ordinary combos like Ctrl+Shift+arrows). Registered as a system hotkey: `RegisterHotKey` on Windows, Carbon's `RegisterEventHotKey` on the Mac. No hook, no permission. Stored as `toggleKey` (platform key code) and `toggleModifiers` (platform modifier flags) in `tools.json`.
  - **Feedback: a small badge 24 px below the cursor**, "Wheel off" (grey dot) / "Wheel on" (accent dot), dark pill 30 px high; fades in 120 ms, holds 900 ms, fades out 250 ms. Same on both. Windows' tray icon also dims to 35% (tooltip "Tool Wheel (off)") while off; the Mac has no menu bar icon.
  - **Off doesn't survive a restart**: the wheel always starts on, so it can't be left off by accident.
  - **No on/off switch in Settings**, only the shortcut field.
  - While off, the wheel shortcut does nothing and a side-button shortcut goes Back/Forward again. Turning it off while the wheel is open closes it without opening anything.
- **Settings has a third row on the right, "Turn wheel on/off"**, between Shortcut and Open apps by: the keys (or "None"), a cross to clear, click to record (Esc cancels). Windows also refuses a combination another app or Windows already has ("Already in use"); the Mac can't tell. The wheel editor moved up 20 px to make room (Windows margin −36 → −76, Mac offset −18 → −38).
- **Clicking one shortcut field while the other is recording switches to it**; clicking the same field again still cancels. Same on both.
- **The wheel's shortcut does nothing while Settings is open** (the user's call), including minimised; a side-button shortcut goes Back/Forward as usual meanwhile. The on/off shortcut still works. Mac and Windows.
- **Windows: the on/off recorder also polls key state**, besides listening for key presses. A combination another app has registered is swallowed before it reaches the window, so the user's Ctrl + F9 (held by some other app at the time) did nothing at all; now it says "Ctrl + F9 is used by another app" (replaces "Already in use").
- **Knob tick scale a little more visible** (the user's ask): major ticks 16% → 26% black at 0.9 → 1.0 px, minor 9% → 16% at 0.6 → 0.7 px; lengths unchanged. Mac, Windows and the website demo. App icons (rendered from the knob) not re-rendered.
- **Windows: a window also counts as a tool's when its .exe is in a subfolder of the tool's folder**, for the open dot and for bringing it forward. Launcher-style apps run their windows from elsewhere: Discord's `Discord.exe` runs `app-<version>\Discord.exe`, Steam's window is `bin\cef\…\steamwebhelper.exe`, Opera's launcher runs `<version>\opera.exe`; on the test PC Discord and Steam were open with no dot. Programs right beside the tool's don't count (Word vs Excel), nor do shared folders (Windows and anything under it, Program Files, Program Files (x86), AppData, AppData\Local\Programs, the user folder).

## Next

**Windows**
- Finish testing on a real PC; likely fixes: DPI/cursor maths across monitors, icon extraction.
- Frosted backdrop (likely needs a WinUI/DWM approach).
- `.lnk` tools (hand-edited JSON only) always start a new copy and get no "open" dot; resolve the shortcut target to match windows.
- Installer (Inno Setup or MSIX) for a Start menu entry and uninstaller; code signing to drop the SmartScreen warning.

**Website**
- Show slot colour bands in the demo wheel.
- Vercel project exists; get it serving `site/` (root `vercel.json`, or Root Directory `site` in the dashboard), then add the URL to the README.

**macOS**
- Offer to move the app into Applications on first launch (avoids App Translocation).
- Notarization, if an Apple Developer account is set up.
- Switch login item to `SMAppService` once properly signed.

## Known limits

- Shortcuts are modifier keys, optionally with mouse side button 4 or 5 (no letter keys, no other mouse buttons) on both platforms; letter keys would need permissions or a hook.
- Mac: the side-button shortcut's Accessibility permission belongs to the app's ad-hoc signature, so a new version may need it granted again (remove and re-add Tool Wheel under Privacy & Security → Accessibility). Mac side-button code is compile-checked only; not yet tried on a Mac.
- Windows: the side-button hook can't see or block clicks on admin windows (same limit as the keys).
- Mac: the on/off shortcut can't be checked against other apps' hotkeys (pressing one that's taken does nothing while recording, with no message), and its code (Carbon hotkey, key names, badge) is compile-checked only; not yet tried on a Mac.
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
