---
spec: 10187
feature: wordmd-windows-store-release
status: draft
agent: emily
drafted: 2026-09-14
revised: 2026-09-14 (Spec Review Fleet round 1: Sage SHIP WITH FIXES; the Model's no-MSIX claim was false and inverted Q1, corrected)
source: spec/punchlist.md (Ideas, 2026-09-13), from processed/2026-09-13-wordmd-windows-store-release.md
size: M
priority: L
---

# WordMD in the Windows Store

**ReboundMan-WordMD** · spec **10187** · `wordmd-feature-research-and-release-in-the-windows-store`

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
2026-09-13 — (M?)(L?) WordMD feature: research and release in the Windows Store _(vault: processed/2026-09-13-wordmd-windows-store-release.md)_
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md` Ideas, dated 2026-09-13, delivered by Cody from vault note `processed/2026-09-13-wordmd-windows-store-release.md`, itself filed from `inbox/raw/` (an Outlook-mobile one-liner). JJ's entire capture, quoted in the note: "Research and release in windows store."

**This is a thin item, and the spec says so rather than inflating it.** One sentence of capture became this stub. Its job is to name the real decisions so JJ can gate a direction, not to pretend a plan exists.

## Problem

WordMD ships today as a **signed Inno Setup `.exe`** via GitHub releases (`installer/WordMD.iss`, AppId `{A6E3D6F8-2B5E-4B10-9C40-7C3F3B5E1D70}`). The app is a WinUI 3 / Windows App SDK app (`UseWinUI=true`, `Microsoft.WindowsAppSDK` 2.0.1, `net8.0-windows10.0.26100.0`, `WinExe`). That is one distribution channel, discovered by people JJ tells directly (`processed/2026-08-16-wordmd-promotion-push`). The Windows Store is a second channel with its own discovery surface, its own trust signal (Store-verified), and its own packaging, submission, and monetization rules. Whether it is worth the work, and in which packaging shape, is undecided.

## Model: what exists today, cited

- App shape: **WinUI 3 / Windows App SDK** (`UseWinUI=true`, `Microsoft.WindowsAppSDK` 2.0.1, `net8.0-windows10.0.26100.0`), not a generic .NET desktop app; this matters because the Store path for a Windows App SDK app is well-trodden.
- Distribution: `installer/WordMD.iss` produces the signed installer; releases are manual GitHub uploads.
- **MSIX scaffolding already exists and is the correction that flips this spec's Q1** (round-1 Sage, load-bearing): `src/WordMD/Package.appxmanifest` is present, and `WordMD.csproj` carries the single-project MSIX tooling (`EnableMsixTooling`, `ProjectCapability Include="Msix"`, `HasPackageAndPublishMenu`). What is NOT true is that it is ready: `WindowsPackageType=None` means the app currently builds unpackaged, and the manifest is stale (`Version="1.0.0.0"` against `VERSION` 2.1.0, untouched since ~v1.4.1). So the MSIX path is "revive and update existing scaffolding," not "create a packaging project from scratch." Only the `.wapproj` half of the draft's original claim was true; the manifest and tooling were already here.
- Signing: the installer is Authenticode-signed (the code-signing path was its own decided spec earlier in this repo's history; `reviews-archive/reboundman-wordmd/spec-decide-code-signing-path-for-the-windows-installer-*` in the vault). A Store submission has a different signing model (Store-managed for MSIX, or the partner-center identity for an unpackaged submission).
- Monetization: WordMD carries a Stripe tip-jar path (`processed/2026-08-03-wordmd-nag-screen-stripe-removal`; the `/wordmd-tip` redirect on reboundman.com). The Store has its own commerce rules that interact with external-payment links, which is a real constraint, not a footnote.

## Charter

Research the Windows Store path for WordMD, then, on JJ's chosen direction, produce a submittable package and a listing. v1 is the research-and-decide half plus the mechanical packaging; it does not commit to a monetization change or to retiring the `.exe` channel.

## Behavior (once JJ picks a direction in the open questions)

1. Produce the packaging the chosen path needs. For the recommended MSIX path (Q1): update the stale `Package.appxmanifest` to the current version and identity, flip the packaged-build configuration on, and produce a signed `.msix`/`.msixbundle` from the existing project, without breaking the existing Inno release. (For the fallback unpackaged path: submit the signed Inno `.exe` through Partner Center as-is.)
2. A Store listing draft (name, description in JJ's voice, screenshots, category, age rating) staged for his review, never submitted by an agent.
3. A short `docs/` note recording the decisions taken and the account/identity facts, so the next release does not re-derive them.

### Explicitly out of scope for v1

- Actually submitting to the Store (JJ's hand and his Partner Center account; agents never publish).
- Removing or changing the Stripe tip-jar (its own decision; the Store's commerce interaction is surfaced in open question 3, not resolved here).
- Retiring the `.exe` channel. v1 assumes both channels coexist, but the standing maintenance cost of two channels (two build-and-sign paths, two version bumps kept in lockstep against `VERSION`, the same alignment discipline the repo's existing version-alignment work already tracks) is a real input to Q1, not a footnote: MSIX buys Store auto-update and a cleaner second channel, which partly offsets that cost.

## Relationships

- `installer/WordMD.iss`: the current channel this adds a sibling to, not a replacement.
- `processed/2026-08-16-wordmd-promotion-push` (vault): the distribution thread this extends; a Store listing is the bigger unbuilt piece of it.
- `processed/2026-08-03-wordmd-nag-screen-stripe-removal` (vault): monetization prep whose Store-commerce interaction open question 3 raises.
- The code-signing spec in this repo's history: the Store changes the signing model, so that decision is context, not a dependency.

## Permissions

N/A for the app's runtime (WordMD runs in the user's own session). The BUILD work is local: the single-project MSIX tooling already in `WordMD.csproj` (or the Store CLI) runs in JJ's dev environment under his existing tooling; no fleet agent runs anything autonomously, no new `.claude/settings.json` rules, no credentials enter any agent environment. The one credentialed act, the Store submission itself, is explicitly JJ's hand in his Partner Center account and is out of scope for the build.

## Acceptance

- The chosen packaging builds from the current source and installs and runs WordMD on a clean Windows 11 machine.
- The existing signed `.exe` release still builds and works unchanged.
- A listing draft exists for JJ's review; nothing is submitted by an agent.
- The decisions and account facts are recorded in `docs/` so the next release is mechanical.

## Open questions

**1. Packaging path: (a) revive the existing MSIX scaffolding and submit an `.msix`; (b) the Store's unpackaged-app submission of the existing signed Inno `.exe`; (c) research-only for now, decide after the facts are in.**

Discovery, corrected after round 1: WordMD is a WinUI 3 / Windows App SDK app, and its repo **already carries single-project MSIX scaffolding** (`Package.appxmanifest`, `EnableMsixTooling`, the Msix ProjectCapability), currently unused (`WindowsPackageType=None`, manifest stale at 1.0.0.0). So MSIX is not "a new packaging project" as the first draft wrongly assumed; it is updating a stale manifest and turning on the packaged build. The Store also accepts unpackaged `.exe`/`.msi` submissions through Partner Center, which would reuse the Inno installer. MSIX gives clean install/uninstall, Store auto-update, and the strongest trust signal; for a WinUI 3 app it is the intended path and the container fit is not the concern it is for a legacy Win32 app. Options: (a) gains the full Store experience off scaffolding that already exists, costs updating the manifest and packaged-build testing; (b) gains a listing off the existing `.exe`, costs the MSIX benefits and still carries the two-channel maintenance; (c) gains not guessing, costs a round-trip. Recommendation: **(a)**, because the earlier reason to prefer the unpackaged path (MSIX means a whole new project) was false: the scaffolding is here, WordMD is exactly the app type MSIX is built for, and MSIX's auto-update is what makes a second channel worth maintaining. Reversal is cheap; a listing can switch packaging later. Teaching: the draft got this backwards because it did not find the manifest already in the tree, which is the whole reason the panel re-reads the repo.

**2. Does a Microsoft Partner Center developer account already exist, or is standing one up part of this? (a) exists; (b) needs creating (one-time fee, identity verification).**

Discovery: this is investigation debt the capture could not answer and the repo cannot show; it is a fact about JJ's Microsoft account, not the code. It is a genuine blocker: no submission path exists without the account, and business-vs-individual verification takes real calendar time. Recommendation: **answer this before any packaging work**, because (b) has a lead time that should start now regardless of the packaging path, and JJ may want it under a JLJ/business identity rather than personal given the WordMD-as-product direction. Teaching: the account, not the package, is the long-pole item.

**3. The Store commerce interaction with the existing Stripe tip jar: (a) keep the tip jar, list as free, accept the Store's external-payment rules as they stand; (b) drop external payment in the Store build; (c) defer, list free with no in-app payment reference at all for v1.**

Discovery: WordMD already routes tips through Stripe (`/wordmd-tip`). Microsoft Store policy on steering users to external payment has shifted repeatedly and differs for a genuinely free app with an optional external donation versus paid features. A free app with a "support the developer" outbound link is generally acceptable, but the exact current rule needs checking against the live policy at submission time, not assumed. Recommendation: **(c) for v1**, list WordMD as free with no in-app payment surface in the Store build, so the listing cannot be held up on a commerce-policy review; revisit the tip jar as its own decision once the app is live in the Store. Reversal is a listing update, cheap. Teaching: monetization is the most common Store-review reject reason, so keeping v1's Store build commerce-free is the low-friction path to actually shipping the listing.

## Spec Review Fleet

Round 1, 2026-09-14: **Sage SHIP WITH FIXES.** The load-bearing fix: the Model claimed no MSIX packaging existed, when `src/WordMD/Package.appxmanifest` and the single-project MSIX tooling are already in the tree (stale, but present), which inverted Q1's recommendation from the unpackaged path to reviving the existing MSIX scaffolding. Also corrected: WordMD is a WinUI 3 / Windows App SDK app (not a plain .NET desktop app), and the two-channel maintenance cost is now a decision input to Q1 rather than a one-line dismissal. Sage confirmed the rest solid: the Store-is-no-longer-MSIX-only point, the Partner-Center-as-long-pole framing, the hedged commerce caution, and the Permissions section. All applied. (Sage only; no Hawk, the spec touches no auth, secrets, endpoints, or agent write scope.)

## Notes for review

Thin by origin: a one-line capture. Size M and priority L are proposals, not confirmations (the punchlist marks them `(M?)(L?)`): M because even the fast path is packaging plus a listing plus testing, and L because WordMD distribution has moved slowly and nothing here is time-pressured. The three open questions are the actual content of this spec; the body around them is grounding, not a plan, and deliberately does not invent one.
