---
spec: 10025
feature: keep-version-and-installer-wordmd-iss-aligned-before-each-re
status: ready
pr: https://github.com/ReboundMan/ReboundMan-WordMD/pull/1
revised: 2026-07-31 (JJ review round 1); 2026-07-31 (Emily round 1 answer: single source verified already implemented, one checklist line remains)
agent: emily
drafted: 2026-07-29
source: spec/punchlist.md (Next)
size: S
priority: H
---

# Keep VERSION and installer\WordMD.iss aligned before each release

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
(S)(H) Keep `VERSION` and `installer\WordMD.iss` aligned before each release. Implemented 2026-07-30 with v1.6.0: the version is single-sourced from the repo-root `VERSION` file (the csproj reads it at build time, `WordMD.iss` reads it at installer compile time, and the app's status bar and About dialog display the assembly version instead of hardcoded strings), and the new `.github/workflows/release.yml` fails the release build if the tag does not match `VERSION`
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md` Next (Up Soon), line dated 2026-07-30. Filed by commit `deb8c81` ("emily: draft specs for code-signing decision and VERSION/installer alignment", 2026-07-29). Section retrofitted 2026-08-02.

## Problem

The version number exists in two places that nothing keeps in sync: the root `VERSION` file and `#define MyAppVersion` inside `installer\WordMD.iss`. Today they agree (both `1.5.0`), but agreement is enforced by a human remembering, not by tooling.

## Charter

Stop the two version declarations from drifting apart, either by generating one from the other or by adding a check that fails loudly when they disagree.

## Model (what the code has today, cited; re-verified 2026-07-31, and it changed since drafting)

**The single-source model is already implemented.** Between this spec's drafting (07-29) and JJ's review (07-31), an ad hoc session shipped exactly option 1: `installer/WordMD.iss:10-11` now reads `#define VerFile FileOpen(AddBackslash(SourcePath) + "..\VERSION")` then `#define MyAppVersion Trim(FileRead(VerFile))`, with the comment at line 8 stating the single-sourcing; and the app binary reads the same file at build via `src/WordMD/WordMD.csproj:26` (`<Version>$([System.IO.File]::ReadAllText(...VERSION).Trim())</Version>`). Drift between VERSION and the installer is now structurally impossible. The paragraphs below record the pre-fix state this spec was drafted against:

- `VERSION:1` contains a single line: `1.5.0`.
- `installer/WordMD.iss:8` declared `#define MyAppVersion "1.5.0"` — then hand-maintained, now superseded as above.
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

## Remaining work (all that is left)

One line: `spec/DEPLOY.md:48` still says "Update the version in `VERSION` and `installer\WordMD.iss` together," which now instructs a hand-edit of a file that would be overwritten by the preprocessor's read. Rewrite step 1 to: "Bump `VERSION`; the installer and app binary both read it at build." That is the whole feature now.

## Open questions

- **JJ, 2026-07-31:** 1. Single source of truth is the right model - define that as the fix
  - *Emily, 2026-07-31: agreed and, on re-verification, already implemented in the code (citations in Model): `WordMD.iss` reads `VERSION` via the ISPP `FileOpen`/`FileRead` at compile, and the csproj reads the same file at build. Your ad hoc session beat the spec to it. The one remaining piece is the stale DEPLOY.md checklist line (Remaining work above). Recommend: fix that line, then mark this spec `built`; alternatively mark `rejected` as overtaken and fix the line by hand, either closes it honestly.*
- ~~Single source vs. automated check~~ answered by JJ above; single source, and it exists.
- ~~Inno preprocessor file-read spike~~ resolved by reality: ISPP `FileOpen`/`FileRead` works and is live at `WordMD.iss:10-11`.
