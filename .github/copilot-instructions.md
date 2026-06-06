# Repo-specific Copilot instructions for WordMD

These override the defaults in `~\.copilot\copilot-instructions.md`.

## Stack

- .NET 8 WinUI 3 desktop app.
- WebView2 editor bundle under `web\` using Milkdown, CodeMirror, TypeScript, and esbuild.
- Inno Setup installer under `installer\`.

## Conventions

- Test runner: no automated test suite is present yet.
- Lint command: no lint command is present yet.
- Build command: `Set-Location web; npm ci; npm run build; Set-Location ..; dotnet build src\WordMD\WordMD.csproj -c Release -p:SkipWebBundle=true`.

## Hard rules

- Never commit secrets. `.env`, `serviceAccountKey.json`, `*.pem`, `*.key`, and related files are gitignored.
- Update `spec\punchlist.md` when completing items.
- Update `CHANGELOG.md` and `VERSION` when shipping a release.
- Keep `VERSION` aligned with `installer\WordMD.iss`.
- Update `spec\card.json` if the public-facing tagline, status, or release URL changes.

## Session start: ProjectPatterns standards are binding

<!-- RM_standards_binding -->
Treat `C:\Users\jeffjame\OneDrive\Code\ProjectPatterns\STANDARDS.md` as binding, not advisory:

- On starting work here, run `Test-ProjectStandards` to see this repo's current score; do not regress any passing standard.
- When you add or change files, re-check the affected standards before you finish.
- Do not invent a new convention when a ProjectPatterns standard already covers it.

## Persona-related

This repo uses the global fleet. See `~\.copilot\AGENTS.md`. Refer to personas by name: Hawk, Bolt, Sage, Forge, Atlas, Lens, Beacon, Rookie, Chaos, and Scout.
