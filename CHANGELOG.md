# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows Semantic Versioning where practical for installer releases.

## [Unreleased]

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
