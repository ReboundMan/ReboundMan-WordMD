# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows Semantic Versioning where practical for installer releases.

## [Unreleased]

### Fixed

- Saving from the Formatted pane no longer restyles content you never touched. The markdown serializer made stylistic choices (bullet character, list tightness, escaping) that did not necessarily match how a file was originally written; editing one paragraph could silently flip every `-` bullet in the file to `*`, or turn a tight list loose. Every top-level block the user did not actually edit now keeps its exact original bytes. Also fixes a real upstream bug (`@milkdown/preset-commonmark`) where tight lists always serialized as loose, and (found in fleet review before this shipped, so never released) a version of this same fix that would have deleted GFM task-list checkboxes and quietly diffed against an already-restyled copy of the file instead of the real original. Design, root causes, and full fleet-review history: `spec/features/preserve-untouched-blocks-on-formatted-save.md`.

## [2.0.1] - 2026-08-12

### Fixed

- Help > About WordMD crashed the app. Present in the published v2.0.0 installer. Root cause and the process changes meant to prevent a repeat: `AGENTS.md`, "C#/WinUI review gap."

## [2.0.0] - 2026-08-12

The first code-signed release: `WordMD-Setup-2.0.0.exe` and `WordMD.exe` are both Authenticode-signed (`CN=ReboundMan.com LLC`, Azure Artifact Signing, Public Trust), so a clean Windows profile should no longer trip the SmartScreen "Windows protected your PC" warning on first run.

### Added

- A richer About dialog (Help > About WordMD) with links to the product page and repo, and an optional tip jar. WordMD is free and stays free; the tip is an optional thanks and opens a Stripe-hosted page in your browser. WordMD holds no payment keys and never sees payment details. Setup runbook: `spec\features\support-wordmd-tip-jar.md`.
- A product page at [reboundman.com/wordmd.html](https://reboundman.com/wordmd.html): why the app exists, what it does, the download, and a feedback form that opens a prefilled GitHub issue.
- Code signing for release builds via Azure Artifact Signing. `tools\build-signed-release.ps1` builds, signs, and verifies both the app executable and the installer against the expected signer identity (not just chain validity), then writes the SHA-256 file. Signing runs locally, by design: no signing credential exists in CI. `spec\DEPLOY.md` carries the release checklist.
- A copy-to-clipboard button on fenced code blocks in the Formatted pane. Hidden from print output.

### Changed

- Formatted-mode print no longer clones the live editing view's DOM. It renders from the document's canonical Markdown into a detached, offscreen view created solely for the print job, so print output stays correct regardless of any future viewport-virtualizing or lazy-loading rendering strategy the live view might adopt.
- The release workflow no longer attaches an installer to a GitHub release. It validates that the tag matches `VERSION` and that the build chain compiles, because CI holds no signing credential and could only ever publish an unsigned binary; the release workflow now runs the same build script CI and local releases share (`-SkipSigning` for CI), so the two can't silently drift apart.

### Fixed

- Alt-Tab showed the wrong icon on some launch paths (pinned taskbar, Start Menu, double-click) even though the taskbar and launch-button icons were always correct. The runtime icon was set from a path resolved against the current working directory, which varies by how an unpackaged app is launched; it now anchors to the running executable's own folder.
- The offscreen print view is now raced against a 5-second timeout, so a pathological document can no longer hang print construction and leak the offscreen instance instead of failing cleanly.
- Clicking Edit in the front-matter panel no longer visibly shrinks the box. The read-only view capped at 160px tall but the edit box only started at 60px, cutting lines out of view the moment you switched into edit mode.

## [1.6.0] - 2026-07-30

### Added

- Front matter is now editable inside WordMD. The banner above the editor expands via its caret to show the YAML, and an Edit checkbox (visible only when expanded) swaps the read-only view for an editor. Escape cancels an edit session; deleting all fields removes the block from the document with the banner staying visible as "Front-matter: removed" until the tab closes. A guard rejects a line of only `---`, which would otherwise corrupt the block boundary on the next open.
- A release workflow (`.github/workflows/release.yml`) now builds the web bundle, publishes the app, compiles the installer, and attaches it with a SHA-256 file to the GitHub release automatically. It fails fast if the release tag does not match `VERSION`.

### Changed

- The product version is single-sourced from the repo-root `VERSION` file: the csproj reads it at build time (status bar and About dialog now display the assembly version instead of hardcoded strings, which had drifted to 1.4.5) and `installer\WordMD.iss` reads it at compile time.
- Dirty-state notifications from the editor to the host are throttled, cutting cross-process messages during fast typing.

### Fixed

- Saving a document whose only change is a front-matter edit no longer re-serializes the untouched body, so those saves change exactly the edited lines.

## [1.5.0] - 2026-06-26

### Added

- Printing: File > Print (Ctrl+P, follows the current view), Print Formatted, and Print Source. The default Print label reflects the resolved target, the menu items are disabled until the editor is ready, the print title sets the default PDF filename, and printing an empty document shows a notice.
- ProjectPatterns standards docs and repo metadata.
- Baseline CI workflow for the web bundle and WinUI project build.

### Changed

- Saving a file now preserves its original UTF-8 BOM instead of stripping it.
- Crash recovery now snapshots every dirty tab (not just the active one) and restores all of them; saving one tab no longer discards another tab's recovery snapshot.

### Fixed

- WebView navigation is locked to the trusted editor page and inbound bridge messages are validated, closing a path where navigated content could overwrite the open file.
- settings.json and recovery snapshots are written atomically, so a crash mid-write no longer resets preferences or corrupts recovery.
- Recovery file names are now collision-free, so documents whose paths share a suffix no longer overwrite each other's snapshot.
- Typing stats are throttled and split-mode scroll-sync avoids per-frame linear scans, reducing input latency on large documents.
- Accessible names added to toolbar buttons, the find bar, and the front-matter toggle; the split divider is keyboard resizable; formatted links are underlined.

## [1.4.5] - 2026-06-05

### Added

- Current installer version recorded from `installer\WordMD.iss`.
