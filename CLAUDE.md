# WordMD Project Context for Claude

## What this is

WordMD is a Windows-only desktop Markdown editor with a native WinUI 3 shell and a WebView2 editing surface.

## Who uses it

Writers, PMs, students, and engineers who want Markdown files to feel closer to editing a Word document.

## Tech notes

- **Stack:** .NET 8, C#, WinUI 3, WebView2, Milkdown, CodeMirror, esbuild, Markdig, Inno Setup.
- **Auth/DB:** None. `auth_family` is `none-local`.
- **Hosting:** None. Releases ship as Windows installer files on GitHub.
- **Local stores:** `%APPDATA%\WordMD\settings.json`, `%LOCALAPPDATA%\WordMD\recovery\`, `%LOCALAPPDATA%\WordMD\telemetry\`, and `%LOCALAPPDATA%\WordMD\feedback\`.

## Conventions specific to this repo

- Build the web bundle from `web\` before building or publishing the WinUI app unless `SkipWebBundle=true` is deliberate.
- Keep `VERSION`, `installer\WordMD.iss`, release notes, and installer names aligned.
- Do not add Firebase, Firestore, or Railway files unless WordMD becomes a hosted service.

## What to read first

- `spec\SPEC.md`: full spec.
- `spec\DEPLOY.md`: release runbook.
- `spec\punchlist.md`: current ideas and bugs.
- `AGENTS.md`: fleet rules.

## What not to touch without asking

- Installer identity values in `installer\WordMD.iss`.
- Local telemetry and feedback privacy behavior.
- `spec\card.json` public-facing metadata.

## House rules inherited

See `~\.copilot\copilot-instructions.md` for default rules. This file overlays project-specific notes only.
