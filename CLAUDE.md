# Tool Wheel

Radial app launcher for macOS (`mac/`, Swift) and Windows (`windows/`, C#/WPF). Read `plan.md` first: it has the status, architecture, every past decision, and what's next.

## Record every decision in plan.md

Whenever a decision is made – by the user or agreed in conversation – add it to the **Decision log** in `plan.md`, in the same turn, before reporting the work as done. This covers design and look, behaviour, architecture, tooling, naming, and release/distribution choices, including reversals ("replaced X with Y").

- Put it under today's date heading (create one if needed, `### YYYY-MM-DD`), newest at the bottom.
- One bullet: **the decision in bold**, then why. If it replaces an earlier one, say what it replaced; don't delete history.
- Also keep **Status**, **Next** and **Known limits** current when work lands or plans change.

## Conventions

- Mac and Windows share the same geometry numbers, accent `#FF3C00`, sound recipe and `tools.json` shape. Change one, change both (or note the gap in `plan.md`).
- Check Mac visuals with `mac/.build/debug/ToolWheel --snapshot out.png`. Windows builds and runs locally on the Windows machine (`cd windows && dotnet build`); CI also compile-checks it via the `Windows` workflow.
- Commits use khan.nibras28@gmail.com (set in local git config). Commit and push only when asked.
- Releases: tag `mac-vX.Y.Z` or `windows-vX.Y.Z`; GitHub Actions builds and attaches the downloads.
