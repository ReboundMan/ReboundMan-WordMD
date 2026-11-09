---
spec: 10248
feature: check-for-updates-follow-ups
status: draft
agent: emily
drafted: 2026-11-08
revised: 2026-11-08 (Spec Review Fleet round 1, Sage SHIP WITH FIXES + Hawk SHIP WITH FIXES: all twelve findings applied, including the release-tag pin source, the -Draft scope, the revocation-timeout conditions, and the UI-free factoring the test claims depend on)
source: spec/punchlist.md (Ideas), item dated 2026-11-08; spec/features/check-for-updates.md ("Fleet review", "Fleet review, round 2", "Not verified", "Later")
size: S
priority: M
---

# Follow-ups from the Check for Updates fleet reviews

**ReboundMan-WordMD** · spec **10248** · `follow-ups-from-the-check-for-updates-fleet-reviews`

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
2026-11-08 - (S)(M) Follow-ups from the Check for Updates fleet reviews, noted 2026-11-08, updated after round 2 (`features/check-for-updates.md`, "Fleet review"). None block the feature. (1) Run the silent update against the real installer and a real older per-user install on a disposable machine or VM (not this one: the real AppId would upgrade the running install). This is the test that decides whether the feature works; see "Not verified" in the spec for the cases, and add the result there. (2) One shared ContentDialog gate for the whole window: only one dialog can be open at a time and several programmatic paths (the file-changed watcher, the recovery prompt, external file activation, the new "last update didn't finish" notice) can collide with any long modal. (3) A Store/MSIX build must not ship the self-updater (no Store build exists yet). (4) Run Narrator, a high-contrast theme, and a mixed app/Windows theme over the update dialogs. (5) Discarding an unsaved tab keeps its crash-recovery snapshot, so the relaunch after an update (or after Exit) can offer to recover text the user threw away; remove the snapshot on Discard, taking care that a later cancel leaves the tab's own snapshot intact. (6) Contract script: a -Draft mode so it can run before --draft=false, and replace its PowerShell copy of the signer logic with a small test project in the repo that links UpdateService.cs and runs the real verification (this would also give the repo its first test project). Also drop the now-redundant warn-only "signed installer attached" step from release.yml. (7) Measure strict revocation against a certificate server that drops packets (a firewall rule on a VM); if it stalls past about 20 s, bound the call with a timeout and fall back. (8) Silent all-users deployment from a SYSTEM-context tool needs commandline added to PrivilegesRequiredOverridesAllowed in installer\WordMD.iss (an installer-identity change, ask first). (9) Restore the open files after a silent update; ADMX template for the policy switch.
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md` Ideas, dated 2026-11-08, written by the Check for Updates build itself. Every sub-item traces to a row in `spec/features/check-for-updates.md` under "Fleet review", "Fleet review, round 2" ("on the punchlist" dispositions), "Not verified", or "Later", **with one exception** (panel round 1, Sage Medium 3): item (6)'s third clause, dropping the warn-only release.yml step, appears in none of those sections; the punchlist line itself introduced it as a consequence of the contract workflow, and the Model below records why it is safe. Size and priority were confirmed on the item, not proposed here.

## Problem

The check-for-updates spec closed with nine named follow-ups scattered across four of its sections and one very long punchlist line. They mix three different kinds of work (code an agent can build, tests only JJ's hands and a VM can run, and changes that need a measurement, an approval, or a design decision first), and nothing today separates them, so the buildable ones sit behind the unbuildable ones.

## Model (what the code has today, cited)

- `src/WordMD/UpdateService.cs` carries the verification logic the follow-ups orbit; `installer/WordMD.iss` (lines 47-48: `PrivilegesRequired=lowest`, `PrivilegesRequiredOverridesAllowed=dialog`, no `commandline`) is what item (8) would change; `.github/workflows/release.yml:73-88` holds the warn-only signed-installer step and `.github/workflows/release-contract.yml` the hard-failing contract check.
- The parent spec's records: round 2's table lists the contract-script `-Draft` mode and the repo test project as "on the punchlist"; "Not verified" carries the silent-path VM test as "the test that decides whether the feature works" (its second bullet); "Later" carries the revocation timeout ("after measuring a dropping network"), the ADMX template, and restoring open files. The snapshot-on-discard issue is round 2's "Open, pre-existing for Exit, more visible now."
- "No automated test project exists in the repo. The harness lives in a scratch folder, not in source control" (check-for-updates.md, "Not verified", last bullet). Item (6)'s test project would be the repo's first.
- Why the release.yml step removal is safe (panel round 1, Sage Medium 3 and Hawk Low 5): the step only warns and checks asset names; `release-contract.yml` hard-fails a missing installer, a missing or malformed `.sha256`, an `-UNSIGNED` asset, a bad signature, a wrong signer, and a version mismatch on the same `release: published` trigger (release-contract.yml:19-21), so it strictly supersedes the warning. **That trigger is now load-bearing**: narrowing the contract workflow to manual dispatch would silently remove the only publish-time check.
- The contract workflow's two-checkout design is deliberate (release-contract.yml:41-56): the checker comes from the default branch, the verified source (the signer pins) from `refs/tags/<tag>`, so the check attests the pins the release actually shipped with, including mid-rotation when the two legitimately differ (check-for-updates.md, Trust model).

## Charter

Split the nine into their honest lanes and make the buildable lane buildable:

- **Agent-buildable now**: (2) the shared ContentDialog gate; (3) the Store/MSIX guard; (5) snapshot removal on Discard with the cancel-keeps-snapshot edge; (6) the contract script's `-Draft` mode, the repo test project, and the `release.yml` warn-only step removal; (9b) the ADMX template. **For (2) and (5), the serialization logic and the discard-snapshot decision must be factored into UI-free classes that the dialogs call** (panel round 1, Sage Medium 1), so the new test project can link and test them the same way it links `UpdateService.cs`; a plain .NET test project cannot instantiate WinUI ContentDialogs, and without this factoring the test claims below are unbuildable.
- **JJ's hands plus a VM**: (1) the real-installer silent-update run, (4) Narrator, high contrast, mixed themes, (7a) the dropped-packets revocation measurement. The spec's deliverable here is a checklist with expected results, written back into check-for-updates.md's "Not verified". The checklist also lists the parent's other VM-dependent rows (per-machine installs and UAC with the dual-registration case; ARM64 and the end-to-end update from a real older release) as optional same-session items, each with its own recorded outcome, because the VM session is the scarce resource and extending it is cheap (panel round 1, Sage Low 6).
- **Gated on measurement, approval, or a named design decision**: (7b) the revocation timeout only if the measurement stalls past about 20 s; (8) the `PrivilegesRequiredOverridesAllowed` installer-identity change, ask first, never built without JJ's explicit yes; (9a) restore-open-files, **gated on a JJ design decision about session-state restoration, to be taken through its own spec**, with the parent's "Later" row as the standing pointer (panel round 1, Sage Low 4). Shipping (8) additionally requires updating the parent's two now-falsified claims (the "/CURRENTUSER would be ignored" statements in its Trust model and round 2 table) and a recorded decision that `ShouldInstallSilently` and the updater's argument list stay unchanged, any later change going back through panel review (panel round 1, Hawk Medium 4).

## Behavior

One PR per lane is the natural shape; the agent-buildable lane is the deliverable, the checklist lane is a markdown addition to check-for-updates.md, and the gated lane waits. Sub-item order inside the buildable lane: (6) first, because the test project then hosts (2)'s and (5)'s tests via the UI-free classes the Charter requires.

## Relationships

- `spec/features/check-for-updates.md`: the parent; every item here cites it, and results from lane two get written back into its "Not verified" section rather than here.
- `spec/punchlist.md` Ideas, "Opt-in automatic update check" (the other new item, same day): deliberately not folded in; it needs its own JJ decision because it makes WordMD contact GitHub without a click, and the parent spec's "Later" section already frames it.
- The punchlist's confirmed size (S) understates lane one if all five buildable items land in one pass; flagged rather than changed, since confirmed characteristics are not Emily's to edit.

## Permissions

- Lanes one and three are repo-local code and workflow edits, built through Hopper's normal PR flow; no new runtime permissions, no new allow rules.
- Item (6)'s `release.yml` edit touches CI workflow files: within Hopper's existing write scope for this repo, and a security surface the panel has now reviewed. The warn-only step is removed **because** `release-contract.yml` hard-fails the same conditions on the same `release: published` trigger, and that trigger is now load-bearing; the PR description says so (panel round 1, Hawk Low 5). The deletion also removes a step that held a `GITHUB_TOKEN`, shrinking the surface.
- Item (8) is explicitly ask-first per the item text: the build never ships it without JJ's recorded yes, and this spec does not constitute that yes.
- Lane two runs on JJ's own machine or VM, outside any agent's scope.

## Acceptance

- The repo has a test project that links `UpdateService.cs` and runs the real signer verification. **When run by `release-contract.yml`, it compiles or links `UpdateService.cs` from the release-src checkout (the tag), not from the checker checkout, and still fails loudly when that file cannot be built or its pins cannot be reached; an old tag whose source cannot compile under the current harness is a loud failure, not a skip** (panel round 1, Hawk High 1).
- The contract script gains `-Draft`, **which skips only the is-published and is-Latest checks; every download, checksum, signature, pin, and version check runs unchanged in draft mode, and the `release: published` trigger path never sets it** (panel round 1, Hawk Medium 2). `release.yml` loses the warn-only signed-installer step.
- The dialog-gate serialization logic lives in a UI-free class with a collision test in the new test project covering at least the watcher-versus-long-modal case; one gate serializes the window's programmatic dialogs.
- The discard-snapshot decision lives in a UI-free class; discarding a tab removes its recovery snapshot, cancelling the discard leaves it intact, and tests in the new test project cover both.
- **The MSIX guard gates the installer hand-off in the update flow (the point that would start Setup), fails closed when package identity is detected, and menu hiding is cosmetic on top; a unit test asserts the service refuses under simulated package identity, faked present and absent; DEPLOY.md records that a future Store build must verify the guard live before shipping** (panel round 1, Sage Medium 2 and Hawk Low 6).
- If the revocation measurement (lane two) stalls past about 20 s and (7b) builds, three conditions hold (panel round 1, Hawk Medium 3): a timeout maps only to the existing revocation-unknown soft-fail path (re-verify without revocation, with full signature, pin, and version checks), never directly to trusted; an explicit Revoked result that has already come back is always honored, never race-discarded; and the abandoned native call's structures stay alive until it actually returns, with its state-release still running then. The timeout value sits well above the measured cold path.
- check-for-updates.md's "Not verified" section has a recorded place for the VM, accessibility, and revocation results, and items (7b), (8), (9a) are marked gated with their gates and owners named.

## Open questions

1. **The decision: (a) build lane one as one PR, or (b) one PR per sub-item.**
   Discovery: the five buildable items share the new test project as infrastructure, and three of them are small enough that separate PRs would each carry more ceremony than change. The UI-free factoring requirement makes (6)-first ordering load-bearing, not just convenient.
   Priced: (a) one review of mixed concerns, reversible; (b) five clean reviews, more nights on the board, also reversible.
   Recommendation: **1a**, with (6) landing first inside it. Teaching: the parent spec's panel already reviewed the behavior of each item; what is left is implementation, so the cheaper review shape wins.
