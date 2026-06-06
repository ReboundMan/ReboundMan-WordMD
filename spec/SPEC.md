# WordMD Specification

| Field | Value |
|---|---|
| Slug | `wordmd` |
| Category | `desktop-app` |
| Status | `active` |
| auth_family | `none-local` |
| Firebase project | `n/a` |
| Domain | `n/a` |
| Railway service | `n/a` |
| GitHub | `ReboundMan/ReboundMan-WordMD` |
| Owner | Jeff |

## Overview

WordMD is a Windows desktop Markdown editor that gives Markdown files a familiar Word-like editing experience. It combines a native WinUI 3 shell with an embedded WebView2 editor powered by Milkdown for formatted editing and CodeMirror for source editing.

## Users and jobs-to-be-done

- **User:** Writers, PMs, students, and engineers who keep notes or documents in Markdown.
- **Job:** Open, edit, preview, and save Markdown files without switching between a raw text editor and a browser preview.
- **Frequency:** Daily or weekly desktop use.

## Scope

**In scope:**
- Native Windows app shell with tabs, toolbar, menus, file association support, and installer packaging.
- Source, formatted, and split Markdown editing modes.
- Local settings, recovery snapshots, opt-in telemetry logs, and local feedback capture.

**Out of scope:**
- Hosted web application behavior.
- Firebase, Firestore, or server-side auth.
- Multi-user cloud document storage.

## Key flows

1. User opens or creates a Markdown file, edits it in source, formatted, or split mode, then saves it back to disk.
2. User sends feedback from the Help menu; WordMD saves it locally and can prefill a GitHub issue.
3. Maintainer builds the web bundle, publishes the .NET app, and packages it with Inno Setup.

## Data model summary

| Store | Shape | Notes |
|---|---|---|
| Markdown files | User-owned `.md` or `.markdown` files | Stored wherever the user chooses. |
| Settings | `%APPDATA%\WordMD\settings.json` | Local preferences such as theme, telemetry opt-in, and recent files. |
| Recovery snapshots | `%LOCALAPPDATA%\WordMD\recovery\` | Local recovery files for crash protection. |
| Telemetry | `%LOCALAPPDATA%\WordMD\telemetry\events-YYYYMMDD.jsonl` | Anonymous, opt-in feature usage only. |
| Feedback | `%LOCALAPPDATA%\WordMD\feedback\feedback-YYYYMMDD.jsonl` | Local feedback submissions, with optional GitHub issue handoff. |

## External integrations

- GitHub Releases for installer distribution.
- GitHub Issues for user feedback handoff.
- GitHub Actions weekly feedback report workflow.
- Inno Setup for Windows installer generation.

## Open questions

- [ ] Decide when the installer should be code-signed.
- [ ] Decide whether feedback should remain GitHub-prefill only or move to a central ReboundMan feedback intake endpoint.

## Decisions log

| Date | Decision | Why |
|---|---|---|
| 2026-06-05 | Declared auth_family as `none-local` | WordMD is a local desktop app with no Firebase or hosted auth surface. |
| 2026-06-05 | Treat Railway and Firestore standards as not applicable | Distribution is a Windows installer, not a hosted service. |
