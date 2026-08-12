# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows Semantic Versioning where practical for installer releases.

## [Unreleased]

### Added

- A richer About dialog (Help > About WordMD) with links to the product page and repo, and an optional tip jar. WordMD is free and stays free; the tip is an optional thanks and appears only once a Stripe Payment Link is configured, so no half-wired button can ship. WordMD holds no payment keys and never sees payment details: the button opens a Stripe-hosted page in your browser. Setup runbook: `spec\features\support-wordmd-tip-jar.md`.
- A product page at [reboundman.com/wordmd.html](https://reboundman.com/wordmd.html): why the app exists, what it does, the download, and a feedback form that opens a prefilled GitHub issue.
- Code signing for release builds via Azure Artifact Signing (`CN=ReboundMan.com LLC`, Public Trust). `tools\build-signed-release.ps1` builds, signs, and verifies both the app executable and the installer, then writes the SHA-256 file. Signing runs locally; `spec\DEPLOY.md` carries the checklist. Not yet exercised on a real release: the first signed installer is the acceptance run, so the currently published v1.6.0 remains unsigned.

### Changed

- The release workflow no longer attaches an installer. It validates that the tag matches `VERSION` and that the build chain compiles, because CI holds no signing credential and could only publish an unsigned binary.

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
