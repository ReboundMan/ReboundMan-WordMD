# WordMD Agent Instructions

This project follows the ReboundMan persona fleet. The global fleet lives in `~\.copilot\AGENTS.md`; this file lists project-specific guidance and the named-persona table for offline reference.

## House standards are binding

<!-- RM_standards_binding -->
This repo operates under `C:\Users\jeffjame\OneDrive\Code\ProjectPatterns\STANDARDS.md`; treat it as binding, not advisory. On starting work, run `Test-ProjectStandards`, do not regress any passing standard, and re-check affected standards when you add or change files.

## Cast (same as global)

| Name | Role | Model |
|---|---|---|
| Hawk | security-auditor | `gpt-5.3-codex` |
| Bolt | performance-reviewer | `gpt-5.3-codex` |
| Sage | sceptical-architect | `claude-opus-4.7` |
| Forge | data-engineer | `claude-opus-4.7` |
| Atlas | ux-ui-researcher | `claude-opus-4.7` |
| Lens | ux-critic | `claude-sonnet-4.6` |
| Beacon | accessibility-reviewer | `claude-sonnet-4.6` |
| Rookie | new-engineer | `claude-haiku-4.5` |
| Chaos | qa-saboteur | `gpt-5.4-mini` |
| Scout | e2e-tester | `claude-sonnet-4.6` |

## Project-specific guidance

- **Stack:** WinUI 3 desktop app on .NET 8 with WebView2, Milkdown, CodeMirror, and esbuild.
- **Auth family:** `none-local`; do not add Firebase or hosted auth without updating `spec\SPEC.md`.
- **Domain:** None. Distribution is through GitHub Releases.
- **Hosting:** None. Railway does not apply.
- **Installer:** Inno Setup reads `installer\WordMD.iss`; keep it aligned with `VERSION`.

## Per-repo persona overrides

None by default. Drop overrides in `.copilot\personas\<name>.md` only if a specific persona should behave differently in this repo.

## Reviews

Fleet output lands in `reviews\<ticket>-<persona>.md` and is gitignored except for `reviews\.gitkeep`.
