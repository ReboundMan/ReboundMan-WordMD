---
spec: 10024
feature: decide-code-signing-path-for-the-windows-installer
status: draft
revised: 2026-08-05 (JJ review round 1); 2026-08-05 (Emily: JJ confirmed Azure Trusted Signing — now branded Artifact Signing — with an existing account, and asked for a full beginner-level walkthrough; Behavior rewritten as concrete numbered steps, researched against current Microsoft Learn documentation); 2026-08-05 (Emily: Spec Review Fleet fixes applied — Sage caught a broken Inno Setup escape sequence and a sign-before-package ordering bug that would both have failed silently on JJ's first real attempt; Hawk caught an overprivileged `az login` credential scope)
agent: emily
drafted: 2026-07-29
source: spec/punchlist.md (Next)
size: M
priority: H
---
# Decide code-signing path for the Windows installer

**ReboundMan-WordMD** · spec **10024** · `decide-code-signing-path-for-the-windows-installer`

**Round 2 (JJ, 2026-08-05): "Yes, absolutely need to move past the warning. I have a Trusted Signing Account with Microsoft Azure... Azure Trusted Signing. And I need a full walk through of the steps I need to follow to get this done - imagine I have never done this before, and need to get to success."** Answered below: the decision from round 1 is settled (buy/use a certificate, Azure Trusted Signing specifically), so this spec is no longer a decision brief — it's an implementation walkthrough. One naming note up front: Microsoft has renamed the service since it originally shipped — "Azure Trusted Signing" is now called **Artifact Signing** in current documentation (`learn.microsoft.com/azure/artifact-signing/`, the old `trusted-signing` URLs now redirect there). Same service, same account you already have; the portal blade and some docs may still say either name depending on when they were last updated. This spec uses "Artifact Signing" going forward since that's what current Microsoft docs call it.

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

* `installer/WordMD.iss:1-80` (round-1 review, Sage, Low: corrected from `1-77` — the file is 80 lines) is the Inno Setup script. Nothing in `[Setup]` or elsewhere references `SignTool`, a certificate, or a `SignedUninstaller`/`SignTool=` directive — confirmed by grep across the repo for `sign|Sign|SmartScreen|certificate|Authenticode`, which returned no hits inside the installer script itself.

* `INSTALL.md:51-53` documents the current state explicitly: "The MVP installer is **unsigned**. Windows SmartScreen will show a 'Windows protected your PC' warning the first time it runs. Click *More info → Run anyway*. Code-signing is on the roadmap for a future release." This punchlist item is that roadmap promise coming due.

* `spec/DEPLOY.md:46-52` is the release checklist (version bump, changelog, build, smoke test, GitHub release). It has no signing step.

* No signing tooling, certificate reference, or CI signing step exists anywhere in the repo (searched `.github/workflows/`, `tools/`, `installer/`) — unverified beyond grep coverage of tracked files; a locally-held certificate outside the repo cannot be ruled out but would not be in version control regardless.

* **No build wrapper script exists** (searched for `.ps1` files repo-wide: only `tools/weekly-feedback-report.ps1`, unrelated). Releases are built by hand per `spec/DEPLOY.md`'s checklist, not a CI release job (`ci.yml` builds and tests on push, per `DEPLOY.md:44`, but doesn't produce or upload the release installer) — so the signing step below is a manual local-build addition, not a pipeline integration.

* **Azure Trusted Signing has been renamed Artifact Signing** by Microsoft since this punchlist item was likely written (checked via Microsoft Learn, 2026-08-05: `azure/artifact-signing/quickstart`, `azure/artifact-signing/how-to-signing-integrations`, both updated 2026-08-03). Same service, same account JJ already has; current setup and integration steps below are sourced from these current docs, not from memory of the older "Trusted Signing" branding, since portal navigation and tooling names can drift between renames.

## Behavior

Decision made (round 2): Azure Artifact Signing, using JJ's existing account. What follows is the concrete path from "account exists" to "signed installer," researched against current Microsoft Learn documentation (`azure/artifact-signing/quickstart` and `azure/artifact-signing/how-to-signing-integrations`, both updated 2026-08-03) since this is a one-time setup most people (including this spec's author) get wrong on the first pass.

**Part A — Azure-side setup (one-time, in the Azure portal)**

1. **Confirm the resource provider is registered.** In the Azure portal, go to **Subscriptions** → your subscription → **Resource providers** (under Settings) → find `Microsoft.CodeSigning` → if it says `NotRegistered`, select it and click **Register**. (JJ said he already has a Trusted Signing account, so this is likely already done — worth a 10-second glance before assuming it needs redoing.)
2. **Confirm the Artifact Signing account exists** (JJ says it does — search "Artifact Signing Accounts" in the portal search bar to find it) and note its **region** (e.g. East US, West US 2) — you'll need the matching endpoint URL later. The account's Overview page shows the region.
3. **Confirm identity validation is Completed.** On the account's Overview page, or under **Objects → Identity validations**, check the status. This is the part that can take 1–20 business days if it hasn't already finished — if JJ set this account up recently and validation is still `In Progress` or `Action Required`, nothing below can produce a real signature until it says `Completed`. **This is the one step in the whole walkthrough Emily cannot verify from here — it needs a JJ eyes-on check of the actual portal**, flagged as Open question 1.
4. **Confirm (or create) a certificate profile.** Under **Objects → Certificate profiles**: if one already exists (from account setup), note its **exact name** — you'll need it verbatim in a config file later. If none exists yet: **Create** → pick a profile type. For a personal tool distributed publicly, **Public Trust** is the one that actually removes the SmartScreen warning (Public Trust Test and Private Trust don't produce a browser/SmartScreen-trusted signature for public distribution). Give it a name (5–100 alphanumeric characters, starts with a letter), select the completed identity validation from step 3, and create it.
5. **Assign yourself the signing role, scoped to the certificate profile, not the account.** Under **Access control (IAM)** on the specific certificate profile (not the account-level IAM blade — round-1 fix, Hawk, Medium: account-level scope is broader than needed and stays that way even after a second, unrelated certificate profile is ever created under the same account), **Add role assignment** → search for **Artifact Signing Certificate Profile Signer** → assign it to your own Azure AD account (the one you'll be signed in as when you run `signtool` locally). Without this role, signing requests are rejected even with a correct config file.

**Part B — local machine setup (one-time, on the machine that builds the installer)**

6. **Install the Artifact Signing Client Tools.** Easiest path, from an elevated PowerShell prompt — but verify the publisher first (round-1 fix, Hawk, Low: this tool becomes a permanent link in every future signed release, worth one extra command to confirm it's really Microsoft's):
   ```
   winget show --id Microsoft.Azure.ArtifactSigningClientTools
   ```
   Confirm the publisher shown is Microsoft, then install:
   ```
   winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
   ```
   This one command installs SignTool.exe (a compatible version — the plain Windows SDK's SignTool may be too old; minimum `10.0.2261.755`), the .NET 8 Runtime, the Visual C++ redistributable, and the Artifact Signing dlib plugin — the four components the manual path (downloading each separately from NuGet) otherwise requires one at a time.
7. **Authenticate with a signing-scoped credential, not your own full Azure login.** **Round-1 fix (Hawk, High):** interactive `az login` puts your full personal Azure AD session — whatever broader access it carries beyond this one signing role — on the same machine that builds releases. If that machine is ever compromised through any vector (a bad dependency, a phished credential, another app on the same box), the attacker inherits everything your identity can touch, not just "can sign WordMD." Create a dedicated, narrowly-scoped credential instead:
   ```
   az ad sp create-for-rbac --name wordmd-artifact-signer --skip-assignment
   az role assignment create --assignee <sp-appId-from-previous-command> --role "Artifact Signing Certificate Profile Signer" --scope <certificate-profile-resource-id-from-step-4>
   ```
   Then authenticate the build machine as that service principal, not as yourself:
   ```
   az login --service-principal -u <appId> -p <cert-or-secret> --tenant <tenantId>
   ```
   If this feels like overkill for a single-developer project, that's a legitimate call to make — but make it consciously (Open question 3, below), not by defaulting to the broadest-privilege option because it's the path of least setup friction.
8. **Create `metadata.json`, kept local-only and gitignored** (round-1 fix, Sage, Low: this was an open question in round 1; resolved here as a decision, not left for JJ, since nothing about it needs his judgment — it's fully reversible and Emily already had a clear recommendation):
   ```json
   {
     "Endpoint": "https://<region-code>.codesigning.azure.net",
     "CodeSigningAccountName": "<your Artifact Signing account name>",
     "CertificateProfileName": "<your certificate profile name from step 4>"
   }
   ```
   Put it somewhere local and gitignored — e.g. `installer\artifact-signing-metadata.json` with a matching `.gitignore` entry (`installer/artifact-signing-metadata.json`), or entirely outside the repo (e.g. `%USERPROFILE%\.wordmd-signing\metadata.json`). The file names which Azure resource to target but carries no secret itself (the RBAC role from step 5 is the actual access control, not this file's contents) — keeping it out of the public repo by default costs nothing and avoids handing an attacker a pre-built target list for social engineering. The `Endpoint` region code must match the account's actual region (step 2) exactly — e.g. West US 2 is `wus2`, East US is `eus`. A mismatched region is the most common failure here and shows up as a 403 error, not a helpful "wrong region" message.

**Part C — wire it into this repo's build**

9. **Test signing manually first**, before touching the Inno Setup script. **This validates the Azure-side setup only — identity validation, the RBAC role, `metadata.json`'s contents, timestamp-server reachability — not the Inno Setup `/S` command syntax step 11 uses** (round-1 fix, Sage, Medium: they're two different escaping dialects; this step passing does not mean step 11 will work, and should not be read as ruling out a step-11-specific failure later):
   ```
   & "<path to signtool.exe from step 6>" sign /v /debug /fd SHA256 /tr "http://timestamp.acs.microsoft.com" /td SHA256 /dlib "<path to Azure.CodeSigning.Dlib.dll from step 6>" /dmdf "<path to metadata.json from step 8>" "<any test .exe, e.g. the built WordMD.exe>"
   ```
   The `/tr` timestamp server is not optional — Artifact Signing certificates are valid for only 3 days, so without a trusted timestamp, a signature (and the installer) would stop validating three days after signing. If this command succeeds, `signtool verify /pa` on the same file should show a valid signature chain.
10. **Sign the app executable BEFORE the Inno Setup build packages it — not "either order."** **Round-1 fix (Sage, High):** the original draft of this step said signing order didn't matter; it does. `[Files]` in `WordMD.iss:52` copies whatever bytes are in the publish folder at the moment `ISCC.exe` runs — sign `WordMD.exe` after that run and the already-built installer still contains the unsigned exe, while the installer wrapper itself still gets a valid signature via `SignTool=` (step 11), so a signature check on the installer alone would look correct while the app inside it is silently unsigned. Two signing calls are needed, in this order:
    - `src\WordMD\bin\Release\net8.0-windows10.0.26100.0\win-x64\publish\WordMD.exe` (the app itself) — **sign this first**, before running `ISCC.exe`, and
    - `dist\WordMD-Setup-<version>.exe` (the Inno Setup output) — signed automatically by `SignTool=` (step 11) as part of the `ISCC.exe` run.
11. **Wire the installer signature into `installer\WordMD.iss`** using Inno Setup's built-in `SignTool=` mechanism, so `ISCC.exe` signs automatically on every build rather than requiring a manual step after: add a `SignTool=` line under `[Setup]` (currently lines 20-42) referencing a named tool defined on the ISCC command line, e.g. add to `[Setup]`:
    ```
    SignTool=artifactsigning
    ```
    and invoke the build with the tool definition supplied as a command-line parameter (so the metadata path and signtool path aren't hardcoded into a script that gets committed), **using Inno Setup's `$q` escape around every embedded path** (round-1 fix, Sage, High: the original draft used bare, unquoted paths inside the outer `/S"..."` quotes — Inno's own docs define `$q` specifically because a bare `"` inside that string terminates it early; `signtool.exe` almost always resolves under a `Program Files`-style path containing a space, which is the default case, not an edge case):
    ```
    & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /S"artifactsigning=$q<path to signtool.exe>$q sign /v /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $q<path to dlib>$q /dmdf $q<path to metadata.json>$q $f" installer\WordMD.iss
    ```
    Inno Setup substitutes `$f` with the path to the file it just built. This signs `dist\WordMD-Setup-<version>.exe` automatically every time the installer is built the normal way — but **run a first test build and check the log for the signtool invocation Inno actually constructed** before trusting it (step 9's manual test proves the Azure side works; it proves nothing about whether this `$q`-escaped string itself parses correctly, which is a separate thing to verify).
12. **Update `spec\DEPLOY.md`'s release checklist** (currently 5 steps, lines 46-52) to insert the signing step explicitly, so a future release doesn't silently skip it: sign `WordMD.exe` **before** the ISCC build (step 10's corrected order), confirm the ISCC command line includes the `$q`-escaped `/S"artifactsigning=..."` parameter when building the installer, and add two smoke-test lines — one confirming the **installer's** digital signature (right-click `dist\WordMD-Setup-<version>.exe` → Properties → Digital Signatures tab), and a second, separate one confirming the **installed app's** signature (`%ProgramFiles%\WordMD\WordMD.exe` or wherever it lands post-install) — round-1 fix (Sage, High): checking only the installer wrapper is exactly the gap that would let an unsigned inner exe ship undetected.

## Relationships

* `spec/DEPLOY.md` (release checklist): a signing decision adds a step here regardless of which path is chosen.

* `installer/WordMD.iss`: the `[Setup]` section is where a `SignTool=` directive would land if signing is chosen.

* No other punchlist item or spec in this repo touches signing or SmartScreen; no contradictions found.

## Acceptance

* `signtool verify /pa` on a freshly-built `dist\WordMD-Setup-<version>.exe` shows a valid, timestamped signature chain, and Windows Explorer's Properties → Digital Signatures tab shows it too.
* `WordMD.exe` — the **installed, post-install copy**, not just the publish-folder copy signed before packaging — carries a valid signature. SmartScreen reputation is tracked per-binary, so the app and the installer both need it, and this must be checked on the binary that actually ships inside the installer, not assumed from the publish-folder copy having been signed.
* `installer\WordMD.iss` builds a signed installer via a normal `ISCC.exe` invocation with the `$q`-escaped `/S"artifactsigning=..."` parameter, without manual post-build signing, and the app exe is signed before that `ISCC.exe` run — never after.
* `spec\DEPLOY.md`'s release checklist names the signing step explicitly (both binaries, correct order, the escaped `/S` parameter) so a future release doesn't silently skip it or get the order wrong.
* The signing credential used on the build machine carries only the Artifact Signing Certificate Profile Signer role (scoped to the certificate profile) — not JJ's full personal Azure access — per whatever Open question 3 resolves to.
* `installer/artifact-signing-metadata.json` (or wherever `metadata.json` lands) is gitignored; `git status` after a real signed build shows it as ignored, not untracked.
* A first-run install on a clean Windows profile no longer shows the SmartScreen "Windows protected your PC" warning. Unverified beyond Microsoft's own documentation: whether Artifact Signing's Public Trust profile clears SmartScreen immediately (as EV certificates historically did) or needs some reputation build-up first — not stated definitively in the docs researched for this spec; worth confirming on the first real signed release rather than assumed.

## Open questions

- **JJ, 2026-08-05:** "Yes, absolutely need to move past the warning. I have a Trusted Signing Account with Microsoft Azure." — Emily: confirmed, this is the decision; walkthrough written above.
- **JJ, 2026-08-05:** "Azure Trusted Signing. And I need a full walk through of the steps I need to follow to get this done - imagine I have never done this before, and need to get to success." — Emily: Behavior above is that walkthrough, 12 numbered steps across account setup, local tooling, and repo wiring, sourced from current Microsoft docs.
- **JJ, 2026-08-05:** "Does this block or trail the VERSION-alignment item? I believe that spec is already live now, so doesn't matter any longer." — Emily: confirmed, `keep-version-and-installer-wordmd-iss-aligned-before-each-re.md` is `status: built`; no ordering conflict, this spec's `installer\WordMD.iss` edit (Behavior 11) is additive to what that spec already shipped.

**1. Is identity validation actually `Completed` on the existing account?**

Discovery: this is the one step in the walkthrough (Behavior, step 3) Emily cannot verify without portal access — JJ said he "has" the account, not that validation finished, and Microsoft's own docs state this can take 1–20 business days. Everything from step 6 onward produces real signatures only if this is done.

Options:
- **(a) JJ checks the portal now** (Artifact Signing account → Overview or Objects → Identity validations) and reports back `Completed` or not, before spending time on Parts B/C. Cost: two minutes. Risk: none.
- **(b) Proceed with Parts B/C regardless and let a failed signing attempt (Behavior step 9) reveal the problem.** Cost: same total work either way, but a failure at step 9 is less obviously "identity validation isn't done" than a status the portal states plainly.

**Emily's recommendation: (a).** A two-minute portal check now is cheaper than debugging a signing failure later without knowing which of several possible causes (region mismatch, wrong role, incomplete validation) produced it.

**2. Public Trust certificate profile — confirm this is what the existing profile is, not Public Trust Test or Private Trust.**

Discovery: Behavior step 4 names Public Trust as the profile type that actually clears SmartScreen for public distribution; Public Trust Test is explicitly for testing (doesn't carry public trust) and Private Trust is for internal/enterprise distribution, not a public GitHub release. If JJ's existing certificate profile was created as one of the other two — plausible if set up for an earlier, different purpose — Behavior steps 6-12 would all complete successfully and still not remove the SmartScreen warning, a confusing, silent failure of the actual goal.

Options:
- **(a) JJ confirms the existing profile's type** (Objects → Certificate profiles → the profile → its type field) before proceeding, or creates a new Public Trust profile if the existing one is wrong. Cost: two minutes to check; profile creation (if needed) is Behavior step 4, already in the walkthrough. Risk: none.
- **(b) Proceed and discover the mismatch only if SmartScreen still warns after everything else works.** Cost: the same, but discovered only after the full walkthrough — the most expensive place to find a wrong assumption.

**Emily's recommendation: (a).** Same shape as question 1 — cheap to check now, expensive to discover as the very last, most confusing possible failure mode.

**3. `az login` (JJ's own full Azure session) or a dedicated service principal scoped to just the Signer role?**

Discovery: Behavior step 7 defaults to a scoped service principal after round-1 review (Hawk, High) flagged interactive `az login` as putting JJ's full personal Azure access on a general-purpose build machine for a task that needs one narrow role. This is a real setup-cost-vs-blast-radius tradeoff, not a fact Emily can resolve alone — it depends on how much JJ trusts the specific machine he builds releases on, and whether extra one-time setup (creating and securing a service principal's credentials) is worth it for a single-developer project.

Options:
- **(a) Service principal, scoped to just the Signer role** (Behavior step 7 as currently written). Cost: one extra one-time setup (`az ad sp create-for-rbac` plus a role assignment), and a service-principal secret or cert to store securely on the build machine instead of relying on an interactive browser login. Benefit: a compromised build machine can only sign WordMD releases, nothing else in JJ's Azure account. Reversible: yes, the service principal can be deleted and access revoked independent of JJ's own account.
- **(b) JJ's own `az login`.** Cost: zero extra setup. Risk: the build machine's cached session carries whatever broader access JJ's own Azure identity has — unknown from here, but for a personal/admin account this is often broad (subscription-level access, other resources, billing). Reversible: yes (revoke the session), but the exposure window exists for as long as the broader session is live on that machine.

**Emily's recommendation: (a), already reflected in Behavior step 7.** The setup cost is one-time and modest; the risk (a compromised build machine inheriting JJ's full Azure access) is open-ended and hard to bound in advance. If JJ judges the build machine trustworthy enough that this is unnecessary caution, (b) is a legitimate, informed choice — but it should be chosen, not defaulted to.

