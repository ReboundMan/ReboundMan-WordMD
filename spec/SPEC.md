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

## Printing

WordMD prints through the WebView2 (Chromium) print dialog, which doubles as the Save as PDF path.

- **Menu:** File contains "Print" (Ctrl+P), "Print Formatted", and "Print Source". The default "Print" item is view sensitive and its label reflects the resolved target: Source view prints raw Markdown, Formatted and Split views print the rendered output. The two explicit items override the default regardless of the current view.
- **Default resolution:** source view maps to source print; formatted and split views map to formatted print.
- **Included content:** source print emits the full file including front-matter. Formatted print intentionally omits front-matter and prints only the rendered body. This asymmetry is deliberate; do not "fix" formatted print to include front-matter without updating this spec and HELP.md.
- **Scope:** printing captures the whole document, not just the visible viewport. Editor chrome (menus, tabs, find bar, the inactive pane) is never printed. The print title is set to the document name so the default PDF filename matches.
- **Readiness and empty docs:** the print items are disabled until the editor is ready; printing an empty document shows a "Nothing to print" notice instead of opening a blank dialog.
- **Non-goals (current):** no in-app print preview, no custom headers, footers, or page numbers, and no per-document print settings.

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
