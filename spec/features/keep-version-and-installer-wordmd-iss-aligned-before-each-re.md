---
feature: keep-version-and-installer-wordmd-iss-aligned-before-each-re
status: draft
agent: emily
drafted: 2026-07-29
source: spec/punchlist.md (Next)
size: S
priority: H
---

# Keep VERSION and installer\WordMD.iss aligned before each release

## Problem

The version number exists in two places that nothing keeps in sync: the root `VERSION` file and `#define MyAppVersion` inside `installer\WordMD.iss`. Today they agree (both `1.5.0`), but agreement is enforced by a human remembering, not by tooling.

## Charter

Stop the two version declarations from drifting apart, either by generating one from the other or by adding a check that fails loudly when they disagree.

## Model (what the code has today, cited)

- `VERSION:1` contains a single line: `1.5.0`.
- `installer/WordMD.iss:8` declares `#define MyAppVersion "1.5.0"` — currently matching, hand-maintained.
- `installer/WordMD.iss:29` uses that value in the output filename: `OutputBaseFilename=WordMD-Setup-{#MyAppVersion}`.
- `spec/DEPLOY.md:48` is the only existing safeguard: release-checklist step 1 reads "Update the version in `VERSION` and `installer\WordMD.iss` together." It is a written reminder, not an automated check — nothing in `.github/workflows/ci.yml` (searched for `VERSION`/`version`, only hits are `.NET`/`Node` tool version pins) or in `installer/WordMD.iss` reads from the `VERSION` file.
- Searched the repo for any script that reads `VERSION` and writes/patches `WordMD.iss` (or vice versa): none found under `tools/`, `installer/`, or repo root. `tools/weekly-feedback-report.ps1` is the only PowerShell script in the repo and is unrelated.
- `CHANGELOG.md:9` shows the `[1.5.0]` entry matches both files as of the last release, so the manual step has held so far — this item is about preventing a future miss, not fixing a current drift.

## Behavior

Either of two mechanical fixes closes the gap; both are small enough that "S" sizing holds regardless of choice:

1. **Single source of truth.** Make `installer\WordMD.iss` read `MyAppVersion` from the `VERSION` file at compile time (Inno Setup supports `#define MyAppVersion FileRead(...)`-style preprocessor tricks, or a pre-build step that regenerates a small `.iss` include from `VERSION`). Removes the second hand-edit entirely.
2. **Automated check.** Add a lightweight release-time check (a one-line PowerShell snippet in `spec/DEPLOY.md`'s build sequence, or a CI step) that compares `VERSION` content against the `MyAppVersion` value in `WordMD.iss` and fails the build/release if they differ. Leaves both files hand-edited but makes drift loud instead of silent.

## Relationships

- `spec/DEPLOY.md` (release checklist): step 1 is the manual process this item either automates or backstops.
- `decide-code-signing-path-for-the-windows-installer.md` (same repo, same run): both land in the release-checklist / installer-build area but are otherwise independent — no ordering dependency.
- No contradictions with `spec/SPEC.md` or other feature specs found.

## Acceptance

- A release where `VERSION` and `installer\WordMD.iss`'s `MyAppVersion` disagree either cannot happen (single-source approach) or is caught before the installer ships (automated-check approach).
- `spec/DEPLOY.md`'s release checklist reflects whichever mechanism is chosen.

## Open questions

- Single source of truth vs. automated check — no strong signal either way from the codebase; this is a small enough repo that either is a low-cost fix. JJ's call.
- If single-source: does Inno Setup's preprocessor support a clean file-read here, or is a pre-build codegen step (e.g. a two-line PowerShell script invoked before `ISCC.exe`) simpler? Unverified — would need a short spike against Inno Setup 6's preprocessor docs.
