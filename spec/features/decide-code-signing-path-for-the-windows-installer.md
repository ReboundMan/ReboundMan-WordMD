---
spec: 10024
feature: decide-code-signing-path-for-the-windows-installer
status: draft
agent: emily
drafted: 2026-07-29
source: spec/punchlist.md (Next)
size: M
priority: H
---

# Decide code-signing path for the Windows installer

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
(M)(H) Decide code-signing path for the Windows installer
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md` Next (Up Soon). Filed by commit `deb8c81` ("emily: draft specs for code-signing decision and VERSION/installer alignment", 2026-07-29). Section retrofitted 2026-08-02.

## Problem

WordMD's installer ships unsigned. Every first run trips Windows SmartScreen's "Windows protected your PC" warning, which is exactly the kind of friction that makes a tool look unfinished to a new user, even a friendly one who was told to expect it.

## Charter

Pick a code-signing approach for `dist\WordMD-Setup-<version>.exe` and wire it into the release steps, so a future release checklist item is "run signed build" instead of "explain the SmartScreen warning."

## Model (what the code has today, cited)

- `installer/WordMD.iss:1-77` is the Inno Setup script. Nothing in `[Setup]` or elsewhere references `SignTool`, a certificate, or a `SignedUninstaller`/`SignTool=` directive — confirmed by grep across the repo for `sign|Sign|SmartScreen|certificate|Authenticode`, which returned no hits inside the installer script itself.
- `INSTALL.md:51-53` documents the current state explicitly: "The MVP installer is **unsigned**. Windows SmartScreen will show a 'Windows protected your PC' warning the first time it runs. Click *More info → Run anyway*. Code-signing is on the roadmap for a future release." This punchlist item is that roadmap promise coming due.
- `spec/DEPLOY.md:46-52` is the release checklist (version bump, changelog, build, smoke test, GitHub release). It has no signing step.
- No signing tooling, certificate reference, or CI signing step exists anywhere in the repo (searched `.github/workflows/`, `tools/`, `installer/`) — unverified beyond grep coverage of tracked files; a locally-held certificate outside the repo cannot be ruled out but would not be in version control regardless.

## Behavior

Not prescribed here — this is a decision item, not a build item. The two live paths, both requiring JJ's input:

1. **Buy a code-signing certificate** (OV or EV) from a CA, sign in the Inno Setup build step (`SignTool=` in `[Setup]`, or a post-build `signtool.exe sign` call in `spec/DEPLOY.md`'s build sequence). EV certs remove the SmartScreen warning near-immediately; OV certs still need reputation to build up.
2. **Accept unsigned and rely on SmartScreen reputation building over time** via download volume, formalizing the current state rather than treating it as provisional. This costs nothing but leaves the warning in place indefinitely for infrequent installs.

A third option — a cheaper OSS-friendly signing path (e.g., a CA offering discounted certs for open-source projects) — may exist but is unverified; no such vendor is referenced anywhere in this repo.

## Relationships

- `spec/DEPLOY.md` (release checklist): a signing decision adds a step here regardless of which path is chosen.
- `installer/WordMD.iss`: the `[Setup]` section is where a `SignTool=` directive would land if signing is chosen.
- No other punchlist item or spec in this repo touches signing or SmartScreen; no contradictions found.

## Acceptance

- JJ has picked one of: buy a certificate and sign, or explicitly accept unsigned-for-now (with a revisit trigger, e.g. "reconsider after N installs" or "reconsider before a wider public push").
- If signing is chosen, `spec/DEPLOY.md`'s release checklist gets a signing step, and the certificate/tooling decision (self-managed vs. a signing-as-a-service vendor) is recorded.

## Open questions

- Is WordMD's distribution scale (personal tool, small install base per `reboundman.com`) large enough to justify a paid certificate right now, or does the SmartScreen warning stay acceptable until distribution grows?
- If signing: self-hosted certificate + local `signtool.exe`, or a cloud signing service (Azure Trusted Signing, SignPath, etc.)? Each has different cost and CI-integration shape.
- Does this block or trail the `installer\WordMD.iss` VERSION-alignment item on this same punchlist ([keep-version-and-installer-wordmd-iss-aligned-before-each-re.md](keep-version-and-installer-wordmd-iss-aligned-before-each-re.md))? They touch the same release checklist section but are otherwise independent.
