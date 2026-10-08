# Tool Wheel

A radial launcher for macOS. Hold **⌃⌥⌘** and a wheel of tools opens around your cursor; hover a tool and click to open it. Release the keys to dismiss.

## Run

```bash
swift run
```

## Configure

Tools live in `~/.config/toolwheel/tools.json` (created with defaults on first open, re-read every time the wheel opens):

```json
[
  { "name": "Safari", "path": "/Applications/Safari.app" },
  { "name": "Docs", "path": "https://developer.apple.com" },
  { "name": "My script", "shell": "~/scripts/foo.sh", "symbol": "hammer" }
]
```

- `path` — app, file, folder, or URL
- `shell` — command run with `zsh -lc`
- `symbol` — optional SF Symbol to override the icon
