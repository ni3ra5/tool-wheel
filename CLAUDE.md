# Tool Wheel

Radial app launcher for macOS (`mac/`, Swift) and Windows (`windows/`, C#/WPF), with a landing page in `site/` (plain HTML/CSS/JS on Vercel). Read `plan.md` first: it has the status, architecture, every past decision, and what's next.

## Record every decision in plan.md

Whenever a decision is made – by the user or agreed in conversation – add it to the **Decision log** in `plan.md`, in the same turn, before reporting the work as done. This covers design and look, behaviour, architecture, tooling, naming, and release/distribution choices, including reversals ("replaced X with Y").

- Put it under today's date heading (create one if needed, `### YYYY-MM-DD`), newest at the bottom.
- One bullet: **the decision in bold**, then why. If it replaces an earlier one, say what it replaced; don't delete history.
- Also keep **Status**, **Next** and **Known limits** current when work lands or plans change.

## Conventions

- **The UI and animations are the same on Mac and Windows** (user's rule). Any change to how the wheel or Settings looks, moves or animates goes into both apps in the same change, with matching timings and easing; never ship it to one platform only. Only platform plumbing (key polling, audio APIs, focus handling, icon loading) may differ.
- Mac and Windows also share the same geometry numbers, accent `#FF3C00`, sound recipe and `tools.json` shape. Change one, change both (or note the gap in `plan.md`). The website's demo wheel (`site/script.js`) uses the same numbers too.
- Check Mac visuals with `mac/.build/debug/ToolWheel --snapshot out.png`. Windows builds and runs locally on the Windows machine (`cd windows && dotnet build`); CI also compile-checks it via the `Windows` workflow.
- Commits use khan.nibras28@gmail.com (set in local git config). Commit and push only when asked.
- Releases: tag `mac-vX.Y.Z` or `windows-vX.Y.Z`; GitHub Actions builds and attaches the downloads.
- Every Mac or Windows release must be downloadable from the landing page. `site/script.js` asks the GitHub API for the newest release and links its zip, so a release needs no site edit **as long as** the tag and file names keep matching what it looks for: Mac tag `mac-v*` (or old `v*`) with a `.zip`; Windows tag `windows-v*` with `*-win-x64.zip`. If a release changes tag or file naming, adds a new download (installer, `.dmg`, ARM-only build), or changes the supported OS versions, update `site/` in the same change. After publishing, open the site and check that both buttons show the new version.
