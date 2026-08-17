---
spec: 10024
feature: decide-code-signing-path-for-the-windows-installer
status: live
live: 2026-08-16 (Q4 resolved by practice: local signing, shipped in DEPLOY.md; CI signing becomes a punchlist item if ever wanted)
revised: 2026-08-05 (JJ review round 1); 2026-08-05 (Emily: JJ confirmed Azure Trusted Signing — now branded Artifact Signing — with an existing account, and asked for a full beginner-level walkthrough; Behavior rewritten as concrete numbered steps, researched against current Microsoft Learn documentation); 2026-08-05 (Emily: Spec Review Fleet fixes applied — Sage caught a broken Inno Setup escape sequence and a sign-before-package ordering bug that would both have failed silently on JJ's first real attempt; Hawk caught an overprivileged `az login` credential scope); 2026-08-11 (JJ walked Part A live: steps 1-5 confirmed and recorded, Open questions 1 and 2 closed, question 3 partially closed; step 5's portal navigation corrected — profile-level IAM does not exist in the portal, profile scope is CLI-only — and account-scope Signer accepted deliberately with a caveat for the planned second certificate profile; step 8 metadata.json filled in with confirmed values)
part_a: complete (2026-08-11)
part_b: complete (2026-08-11) — client tools 0.1.128, paths recorded in step 6, Azure CLI installed (the omitted prerequisite) and authenticated via `az login` + interactive tenant selection, metadata.json at C:\Code\Signing\.wordmd-signing\ (endpoint wus2)
part_c: complete (2026-08-11) — step 9 verified against a test exe (CN=ReboundMan.com LLC chaining to Microsoft Identity Verification Root CA 2020, timestamped, signtool verify /pa clean); steps 10-12 implemented via #ifdef SIGN-guarded SignTool= in WordMD.iss, tools\build-signed-release.ps1, release.yml demoted to build validation, and a rewritten DEPLOY.md checklist. UNVERIFIED END TO END: no signed installer has been produced yet — this machine has no .NET SDK and no Inno Setup, so the publish and ISCC steps of the script are untested. First signed release is the acceptance run.
open_questions_status: 1 closed, 2 closed, 3 closed (all 2026-08-11)
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

<<\<UNTRUSTED PUNCHLIST CONTENT
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
5. **Assign the signing role: Artifact Signing Certificate Profile Signer.** Neither Owner nor Identity Verifier can sign — only this role authorizes signing requests; without it they're rejected even with a correct config file. Portal-navigation correction (JJ walkthrough, 2026-08-11): the portal has no IAM blade on an individual certificate profile — only the account-level **Access control (IAM)** blade exists in the portal, and profile-level scope is CLI-only, per `tutorial-assign-roles` ("For more granular access control on the certificate profile level, you can use the Azure CLI").

   **Decision (JJ, 2026-08-11): account-level scope via the portal, consciously accepted.** On the account's **Access control (IAM)** blade: **Add** → **Add role assignment** → **Artifact Signing Certificate Profile Signer** → assign. JJ assigned his own user this way (it's a single-owner account), settling the Hawk round-1 scoping question for the user identity: account scope, chosen deliberately, not defaulted into.

   **Standing caveat for the planned second certificate profile:** JJ expects another app's certificate profile under this same account in the future. Account-scope Signer will cover that profile too, automatically. When that second profile is created, either re-confirm account scope is still intended, or move to per-profile CLI scoping at that point:

   ```
   az role assignment create --assignee <objectId> --role "Artifact Signing Certificate Profile Signer" --scope "/subscriptions/<subscriptionId>/resourceGroups/<rg>/providers/Microsoft.CodeSigning/codeSigningAccounts/<account>/certificateProfiles/<profile>"
   ```

   (`az ad signed-in-user show --query id -o tsv` gives your own objectId; the scope string doubles as the `<certificate-profile-resource-id>` step 7 needs.) Note the step 7 service principal needs this same role too (its assignment scope is its own decision at step 7 time); assigning your own user, as done here, is what lets step 9's manual test run under your own login.

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

   **Where it actually lands (JJ's machine, verified 2026-08-11, package version 0.1.128).** The MSI records no `InstallLocation`, and the two halves go to two different, non-obvious places — neither under `%ProgramFiles%\...\ArtifactSigning`, which is where most people look first:

   | Component   | Path                                                                                                                                                                                                                  |
   | ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
   | dlib plugin | `C:\Users\<you>\AppData\Local\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll`                                                                                                               |
   | SignTool    | `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe` (installed by the bundled "Windows SDK Signing Tools" dependency; ProductVersion `10.0.26100.4188`, well past the `10.0.2261.755` minimum) |

   Take the **x64** SignTool, not the `arm64` or `x86` siblings in the same SDK bin folder. Note the dlib sits beside `Azure.CodeSigning.Dlib.Core.dll`; `/dlib` wants the plain `Azure.CodeSigning.Dlib.dll`.

   To rediscover these on a different machine rather than guessing, the reliable route is the installer's own folder registry (the MSI leaves `InstallLocation` empty, and `where.exe` won't find either file since neither is on `PATH`):

   ```
   (Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders').GetValueNames() | Where-Object { $_ -like '*rtifactSigning*' }
   Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe | Where-Object FullName -like '*\x64\*'
   ```
7. **Authenticate the build machine.** **Decision (JJ, 2026-08-11): interactive** **`az login`** **as himself — the service principal is deferred, not rejected.** JJ's reasoning: overkill for two small personal apps; revisit if this grows into a larger company. This is exactly the informed-choice branch Open question 3 named, so it closes that question rather than overriding it. So:

   **Prerequisite the original step omitted (found the hard way, 2026-08-11): Azure CLI must actually be installed.** It was not on JJ's machine — no `az.cmd` in any standard location, no `%USERPROFILE%\.azure` profile — so `az login` silently did nothing ("az login doesn't do anything", JJ) and step 9 failed at authentication:

   ```
   winget install -e --id Microsoft.AzureCLI
   ```

   Open a **new** shell afterward so `az` lands on `PATH`. Then log in and **select the correct tenant**:

   ```
   az login
   ```

   **What actually worked (JJ, 2026-08-11): plain `az login`, then picking the tenant from the CLI's interactive tenant list.** Current `az` versions enumerate the tenants the account can reach and prompt, which resolves the problem below without needing the flag. If that prompt doesn't appear, or the wrong tenant is selected, name it explicitly:

   ```
   az login --tenant <tenant-id>
   ```

   **`--tenant` is not optional here, and its absence fails confusingly.** A bare `az login` (and the dlib's own interactive fallback) targets the `/common` endpoint, which resolves a personal Microsoft account — JJ signs in as `jeff.james@hotmail.com` — to its **consumer tenant, shown as "Microsoft Services"**, not to the Entra tenant holding the subscription. The Azure CLI application isn't available in that consumer tenant, so the browser returns: *"Selected user account does not exist in tenant 'Microsoft Services' and cannot access the application '04b07795-8ddb-461a-bbee-02f9e1bf7b46'."* That GUID is the Azure CLI's well-known client ID, which is also what `Azure.Identity`'s `InteractiveBrowserCredential` uses by default — so **this error is proof the dlib loaded and reached the auth stage**, not evidence of a dlib problem. Find the tenant ID at portal → **Microsoft Entra ID** → **Overview** → **Tenant ID** (the `*.onmicrosoft.com` primary domain also works).

   The Signer role JJ assigned himself at step 5 is what authorizes the signing.

   **How the dlib picks a credential, since this defines what "logged in" means.** Per `how-to-signing-integrations`, the dlib authenticates with [`DefaultAzureCredential`](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential), trying credential types in order until one succeeds. With no CLI installed, `AzureCliCredential` is skipped and the chain falls through to `InteractiveBrowserCredential` — which is how JJ landed in an unwanted browser flow while believing he was on the `az login` path. Once `az login --tenant` succeeds, the chain stops at `AzureCliCredential` and signing is non-interactive.

   **Optional hardening, only after a signature succeeds:** pin the credential rather than walking the chain, by adding `ExcludeCredentials` to `metadata.json` (step 8) listing every type ahead of `AzureCliCredential`:

   ```json
   "ExcludeCredentials": ["ManagedIdentityCredential","WorkloadIdentityCredential","SharedTokenCacheCredential","VisualStudioCredential","VisualStudioCodeCredential","AzurePowerShellCredential","AzureDeveloperCliCredential","InteractiveBrowserCredential"]
   ```

   Excluding `InteractiveBrowserCredential` before the CLI path is proven removes the working fallback, so sequence matters.

   **What this accepts, stated plainly so the revisit trigger is recognizable.** The build machine now holds a cached session carrying JJ's full Azure access, which includes **Owner at subscription scope** (confirmed in the portal at step 5). A compromise of this machine therefore reaches the whole subscription, not just "can sign WordMD." That is a bounded risk for a single-developer machine JJ controls, and an unbounded one the moment any of these become true — treat any of them as the signal to implement the deferred service principal:

   * a second person, or any CI runner / cloud build agent, needs to sign;

   * signing moves off JJ's personal workstation (e.g. into the `release.yml` GitHub Actions job, which would need a credential in repo secrets — never JJ's own login);

   * the subscription grows to hold anything whose loss would matter independently of WordMD.

   **Round-1 note (Hawk, High), retained as the rationale for the deferred option:** interactive `az login` puts a full personal Azure AD session on a general-purpose build machine for a task needing one narrow role. The deferred implementation, for when a trigger above fires:

   ```
   az ad sp create-for-rbac --name wordmd-artifact-signer --skip-assignment
   az role assignment create --assignee <sp-appId-from-previous-command> --role "Artifact Signing Certificate Profile Signer" --scope <certificate-profile-resource-id-from-step-4>
   az login --service-principal -u <appId> -p <cert-or-secret> --tenant <tenantId>
   ```
8. **Create** **`metadata.json`, kept local-only and gitignored** (round-1 fix, Sage, Low: this was an open question in round 1; resolved here as a decision, not left for JJ, since nothing about it needs his judgment — it's fully reversible and Emily already had a clear recommendation):

   ```json
   {
     "Endpoint": "https://<region-code>.codesigning.azure.net",
     "CodeSigningAccountName": "<your Artifact Signing account name>",
     "CertificateProfileName": "<your certificate profile name from step 4>"
   }
   ```

   For JJ's confirmed Part A values (2026-08-11), that is:

   ```json
   {
     "Endpoint": "https://wus2.codesigning.azure.net",
     "CodeSigningAccountName": "reboundman-signing",
     "CertificateProfileName": "WordMD-PublicTrustCert"
   }
   ```

   **Done (JJ, 2026-08-11):** **`C:\Code\Signing\.wordmd-signing\metadata.json`**, written with exactly the values above. JJ chose a location outside every git repo and outside OneDrive, so no `.gitignore` entry is needed and nothing syncs it to the cloud — a cleaner outcome than the in-repo-plus-gitignore option below. Steps 9 and 11 reference this path as `/dmdf`.

   (Original guidance, for other machines: put it somewhere local and gitignored — e.g. `installer\artifact-signing-metadata.json` with a matching `.gitignore` entry (`installer/artifact-signing-metadata.json`), or entirely outside the repo (e.g. `%USERPROFILE%\.wordmd-signing\metadata.json`).) The file names which Azure resource to target but carries no secret itself (the RBAC role from step 5 is the actual access control, not this file's contents) — keeping it out of the public repo by default costs nothing and avoids handing an attacker a pre-built target list for social engineering. The `Endpoint` region code must match the account's actual region (step 2) exactly — e.g. West US 2 is `wus2`, East US is `eus`. A mismatched region is the most common failure here and shows up as a 403 error, not a helpful "wrong region" message.

**Part C — wire it into this repo's build**

9. **Test signing manually first**, before touching the Inno Setup script. **This validates the Azure-side setup only — identity validation, the RBAC role,** **`metadata.json`'s contents, timestamp-server reachability — not the Inno Setup** **`/S`** **command syntax step 11 uses** (round-1 fix, Sage, Medium: they're two different escaping dialects; this step passing does not mean step 11 will work, and should not be read as ruling out a step-11-specific failure later):

   ```
   & "<path to signtool.exe from step 6>" sign /v /debug /fd SHA256 /tr "http://timestamp.acs.microsoft.com" /td SHA256 /dlib "<path to Azure.CodeSigning.Dlib.dll from step 6>" /dmdf "<path to metadata.json from step 8>" "<any test .exe, e.g. the built WordMD.exe>"
   ```

   **Three traps that all produce misleading output, hit live on JJ's first attempt (2026-08-11).** Paste the env-var form below *verbatim*; hand-substituting paths is what caused all three:

   - **Doubled quotes silently split paths at spaces.** `""C:\...\Start Menu\...""` is not a quoted argument in PowerShell — the `""` pairs collapse and the space splits the path into two arguments. Tell-tale: two `File not found` errors whose text, concatenated, forms the one real path. Paths without spaces (e.g. the `metadata.json` path) survive this, so the mistake looks partly-working.
   - **`.lnk` files are not signable.** Authenticode covers PE files (`.exe`, `.dll`, `.msi`, `.cab`). A Start Menu shortcut fails even with correct quoting. Sign a throwaway *copy* of a real exe.
   - **A bad `/dlib` path degrades to local-store signing instead of erroring usefully.** With an invalid dlib path (JJ's had the literal `<you>` placeholder left in), signtool falls back to enumerating the local personal certificate store and will happily select an unrelated self-signed dev cert. **How to tell the difference:** a genuine Artifact Signing run selects a cert whose subject is your validated `CN=` (e.g. `CN=ReboundMan.com LLC`), issued by a Microsoft CA, expiring in **~3 days**. Local-store fallback shows self-signed certs with GUID subjects and multi-year expiries. A "successful" sign against the wrong cert is the worst outcome here, because it clears every check except the one that matters.

   With JJ's verified paths (2026-08-11) filled in, this is paste-ready — point the last argument at a throwaway copy of any exe first, not the real publish output:

   ```
   & "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" sign /v /debug /fd SHA256 /tr "http://timestamp.acs.microsoft.com" /td SHA256 /dlib "$env:LOCALAPPDATA\Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll" /dmdf "C:\Code\Signing\.wordmd-signing\metadata.json" "C:\Code\Signing\signtest\signtest.exe"
   ```
   `C:\Code\Signing\signtest\signtest.exe` is a throwaway copy of the installed WordMD.exe, staged 2026-08-11 and confirmed unsigned (`signtool verify /pa` → "No signature found"), so a successful run here is unambiguous. `az login` must have happened in the session first (step 7). Then verify:
   ```
   & "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe" verify /pa /v "C:\Code\Signing\signtest\signtest.exe"
   ```
   A region mismatch between `metadata.json`'s `Endpoint` and the account's actual region surfaces as a bare **403**, not a helpful message — that is the first thing to check on failure. (JJ's account: `wus2`, corrected from `wus` during setup; West US and West US 2 are different endpoints.)

   **DONE and verified (JJ, 2026-08-11).** `Signing completed with status 'Succeeded' in 6.15s`, dlib version 1.0.119, `Number of files successfully Signed: 1`. `signtool verify /pa /v` then confirmed a real Public Trust chain, which is the check that distinguishes success from the local-store fallback trap above:

   | Chain element | Value |
   |---|---|
   | Leaf | `CN=ReboundMan.com LLC`, expires **Fri Aug 14 2026** (the expected ~3-day window) |
   | Issuing CA | `Microsoft ID Verified CS EOC CA 03` → `Microsoft ID Verified Code Signing PCA 2021` → `Microsoft Identity Verification Root Certificate Authority 2020` |
   | Timestamp | `Microsoft Public RSA Time Stamping Authority`, stamped Tue Aug 11 19:08:58 2026 — this is what keeps the signature valid past the 3-day cert lifetime |
   | Result | `Successfully verified`, 0 warnings, 0 errors |

   The `/tr` timestamp server is not optional — Artifact Signing certificates are valid for only 3 days, so without a trusted timestamp, a signature (and the installer) would stop validating three days after signing. If this command succeeds, `signtool verify /pa` on the same file should show a valid signature chain.
10. **Sign the app executable BEFORE the Inno Setup build packages it — not "either order."** **Round-1 fix (Sage, High):** the original draft of this step said signing order didn't matter; it does. `[Files]` in `WordMD.iss:52` copies whatever bytes are in the publish folder at the moment `ISCC.exe` runs — sign `WordMD.exe` after that run and the already-built installer still contains the unsigned exe, while the installer wrapper itself still gets a valid signature via `SignTool=` (step 11), so a signature check on the installer alone would look correct while the app inside it is silently unsigned. Two signing calls are needed, in this order:

    * `src\WordMD\bin\Release\net8.0-windows10.0.26100.0\win-x64\publish\WordMD.exe` (the app itself) — **sign this first**, before running `ISCC.exe`, and

    * `dist\WordMD-Setup-<version>.exe` (the Inno Setup output) — signed automatically by `SignTool=` (step 11) as part of the `ISCC.exe` run.
11. **Wire the installer signature into** **`installer\WordMD.iss`** using Inno Setup's built-in `SignTool=` mechanism, so `ISCC.exe` signs automatically on every build rather than requiring a manual step after: add a `SignTool=` line under `[Setup]` (currently lines 20-42) referencing a named tool defined on the ISCC command line, e.g. add to `[Setup]`:

    ```
    SignTool=artifactsigning
    ```

    and invoke the build with the tool definition supplied as a command-line parameter (so the metadata path and signtool path aren't hardcoded into a script that gets committed), **using Inno Setup's** **`$q`** **escape around every embedded path** (round-1 fix, Sage, High: the original draft used bare, unquoted paths inside the outer `/S"..."` quotes — Inno's own docs define `$q` specifically because a bare `"` inside that string terminates it early; `signtool.exe` almost always resolves under a `Program Files`-style path containing a space, which is the default case, not an edge case):

    ```
    & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /S"artifactsigning=$q<path to signtool.exe>$q sign /v /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib $q<path to dlib>$q /dmdf $q<path to metadata.json>$q $f" installer\WordMD.iss
    ```

    Inno Setup substitutes `$f` with the path to the file it just built. This signs `dist\WordMD-Setup-<version>.exe` automatically every time the installer is built the normal way — but **run a first test build and check the log for the signtool invocation Inno actually constructed** before trusting it (step 9's manual test proves the Azure side works; it proves nothing about whether this `$q`-escaped string itself parses correctly, which is a separate thing to verify).
12. **Update** **`spec\DEPLOY.md`'s release checklist** (currently 5 steps, lines 46-52) to insert the signing step explicitly, so a future release doesn't silently skip it: sign `WordMD.exe` **before** the ISCC build (step 10's corrected order), confirm the ISCC command line includes the `$q`-escaped `/S"artifactsigning=..."` parameter when building the installer, and add two smoke-test lines — one confirming the **installer's** digital signature (right-click `dist\WordMD-Setup-<version>.exe` → Properties → Digital Signatures tab), and a second, separate one confirming the **installed app's** signature (`%ProgramFiles%\WordMD\WordMD.exe` or wherever it lands post-install) — round-1 fix (Sage, High): checking only the installer wrapper is exactly the gap that would let an unsigned inner exe ship undetected.

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

* A first-run install on a clean Windows profile no longer shows the SmartScreen "Windows protected your PC" warning. **Answered, 2026-08-12, on the actual v2.0.0 release:** confirmed needs reputation build-up, not immediate — Public Trust is not the EV tier that gets instant SmartScreen trust regardless of volume. Downloading `WordMD-Setup-2.0.0.exe` in Edge showed the browser's separate *file-download* reputation warning ("isn't commonly downloaded... make sure you trust it before you open it"), driven by per-SHA-256 download volume, not by the Authenticode signature itself. This is a different, milder mechanism than the OS *execution-time* block this item was written to fix (an advisory with Keep/Delete, not a full-screen block requiring "More info → Run anyway"). **Still open:** whether the execution-time "Windows protected your PC" screen itself is actually gone now — that's the metric this item was written against, and it wasn't yet confirmed by clicking through to actually run the installer. Confirm on the next real install.

## Open questions

**4. Does signing happen locally or in CI? Raised 2026-08-11, blocks steps 10-12.**

Discovery: this spec was written (2026-08-05) when "no release CI existed" was a cited fact of the codebase (see Model, "No build wrapper script exists"). That changed on **2026-07-30**, before Part A was walked: `.github/workflows/release.yml` now builds the web bundle, publishes the app, **compiles the installer with ISCC**, and attaches it to the GitHub release. Step 11 adds `SignTool=artifactsigning` to `installer\WordMD.iss`, and Inno Setup requires a `SignTool=`-named tool to be supplied at compile time (`/S"name=..."`). CI passes no such parameter and, under step 7's no-service-principal decision, holds no credential that could sign. **So step 11 as written breaks the automated release path** — the ISCC step fails, and the release ships no installer at all.

Options:
- **(a) Local signing; CI stops producing the published installer.** Keep `az login` (step 7 unchanged). `release.yml` either drops the ISCC/upload steps or keeps building unsigned purely as a build check, and JJ builds + signs + `gh release upload`s locally. Cost: the v1.6.0 release automation is partly undone; a manual step returns to every release. Benefit: zero new credentials anywhere; the signing key path stays entirely on JJ's machine.
- **(b) CI signs via GitHub OIDC federated credentials.** Register a federated identity credential on a service principal scoped to the certificate profile, use `azure/login` (or the official `Azure/artifact-signing-action`), and **store no secret at all** — GitHub exchanges a short-lived OIDC token. Cost: the step 7 service principal work, plus federated-credential setup. Benefit: fully automated signed releases and no long-lived secret in repo settings.
- **(c) CI signs via a service principal secret in GitHub Actions secrets.** Same as (b) but with a stored client secret. Cost: same setup minus federation. Risk: a long-lived credential living in repo settings, with rotation to remember.

Note this is precisely the trigger step 7 named ("signing moves off JJ's personal workstation, e.g. into the `release.yml` GitHub Actions job, which would need a credential in repo secrets — never JJ's own login"), so choosing (b) or (c) reopens Open question 3 by design rather than contradicting it.

**CLOSED 2026-08-11: (a), local signing, chosen by JJ.** Implemented the same session:
- `installer\WordMD.iss` gained a **`#ifdef SIGN`-guarded** `SignTool=artifactsigning` plus `SignedUninstaller=yes`. The guard is what keeps option (a) workable: a plain `ISCC` run still compiles unsigned (so CI can validate the chain), while `/DSIGN` produces the signed build. Without the guard, ISCC aborts on every invocation that doesn't supply the named tool.
- `tools\build-signed-release.ps1` is the "run signed build" step the Charter asked for. It preflights the four paths plus the `az` login, builds web + publish, signs `WordMD.exe` **before** ISCC, builds with `/DSIGN`, verifies **both** binaries, and writes the `.sha256`. It assembles the `/S"artifactsigning=..."` parameter with `$q` escapes via a single-quoted template (so `$q`/`$f` aren't PowerShell-expanded) and runs ISCC through a temp `.cmd` so PowerShell's native-argument re-quoting can't mangle the embedded quotes.
- `.github\workflows\release.yml` renamed to **Release build validation**: it keeps the tag/`VERSION` check and compiles the installer unsigned as a build check, but **no longer attaches it**, since CI could only ever publish an unsigned binary. Its permissions dropped from `contents: write` to `contents: read`.
- `spec\DEPLOY.md` release checklist rewritten around the script, with a Code signing section and three new smoke-test lines (installer signature, installed-app signature, SmartScreen).

**Fleet review of that implementation, same day (`reviews/code-signing-local-build-*.md`: Hawk, Sage, Chaos, Rookie, Bolt).** All findings applied:
- **Identity, not just validity** (Hawk Medium + Sage High, independently). `signtool verify /pa /q` proves the chain is trusted but never checks *who* signed, so the local-store fallback this spec calls the worst outcome would pass if a dev cert sat in Trusted Root. Verification now uses `Get-AuthenticodeSignature` and asserts `Status -eq Valid`, that the subject contains the expected identity, and that a timestamp exists. Exercised against the real signed test exe, including confirming a wrong expected-subject is rejected.
- **Unsigned output renamed** (Sage High). `WordMD.iss` now emits `WordMD-Setup-<version>-UNSIGNED.exe` when `SIGN` isn't defined. Identical filenames were the actual hazard: `#ifdef SIGN` protects the *build*, but nothing stopped a plain-ISCC artifact from being uploaded, and `DEPLOY.md` documented exactly that build.
- **CI runs the same script** (Sage High). `release.yml` invoked a hand-copied twin of the release chain, duplicating publish args and the TFM path; it now calls `tools\build-signed-release.ps1 -SkipSigning`, so CI cannot drift green against a build that no longer ships.
- **Broken workflow step removed** (Sage High). The asset-reminder step's backslash-escaped `jq` expression was invalid, and pwsh propagates the exit code, so the workflow would have gone red on every release. Rewritten with `--jq` and proper quoting, warning rather than failing since it runs post-publish.
- **`-SkipWebBundle` was a no-op** (Bolt). `dotnet publish` omitted `-p:SkipWebBundle=true`, so the csproj's `BuildWebBundle` target rebuilt the bundle on every publish, building it twice per run and making the switch a lie. Now always passed.
- **Draft-first release ordering** (Sage Medium). Publishing before uploading makes an assetless `releases/latest` the default state; the script and `DEPLOY.md` now prescribe create-draft → upload → publish.
- **Stale-output and non-ASCII guards** (Chaos). The target installer is deleted before ISCC and asserted newer than the run start, so a previous build can't be verified as this run's output; non-ASCII tool paths are rejected up front, since the temp `.cmd` is ASCII and a mangled dlib path lands straight in the local-store fallback. Chaos's BOM and spaces-in-TEMP findings were tested and did not reproduce under PowerShell 7 (`Get-Content -Raw` strips the BOM; `cmd.exe /c` handled a spaced path and propagated the exit code).
- **Tool discovery and docs** (Rookie). Hardcoded a single Windows SDK build number; now discovers the newest SDK, `ISCC`, and `az`, with explicit overrides. `DEPLOY.md` gained `metadata.json` setup, the `/DSIGN` explanation, success criteria, and the tag-must-equal-`v$VERSION` rule; it also no longer names the Azure account and profile in the public repo (Hawk Low: inconsistent with keeping those same values out of git).
- **Workflow injection** (Hawk Low). `github.event.release.tag_name` was interpolated into `pwsh` bodies; now passed via `env:`.

**Emily's recommendation was: (a) now, (b) when it starts to chafe.** Rationale: (a) preserves the just-verified working setup and adds no credential surface, and the manual cost is one command per release for a project that releases occasionally. (b) is the right long-term answer and is strictly better than (c) — never choose (c) over (b), since OIDC removes the stored secret for roughly the same setup effort. Deciding (a) does not waste work: steps 10-12 are written the same way either path, only the ISCC invocation site differs.

* **JJ, 2026-08-05:** "Yes, absolutely need to move past the warning. I have a Trusted Signing Account with Microsoft Azure." — Emily: confirmed, this is the decision; walkthrough written above.

* **JJ, 2026-08-05:** "Azure Trusted Signing. And I need a full walk through of the steps I need to follow to get this done - imagine I have never done this before, and need to get to success." — Emily: Behavior above is that walkthrough, 12 numbered steps across account setup, local tooling, and repo wiring, sourced from current Microsoft docs.

* **JJ, 2026-08-05:** "Does this block or trail the VERSION-alignment item? I believe that spec is already live now, so doesn't matter any longer." — Emily: confirmed, `keep-version-and-installer-wordmd-iss-aligned-before-each-re.md` is `status: built`; no ordering conflict, this spec's `installer\WordMD.iss` edit (Behavior 11) is additive to what that spec already shipped.

### Part A progress (JJ walkthrough, 2026-08-11) — questions 1, 2, and the user half of 3 are now closed

JJ worked Part A live and confirmed each step. Recorded here so a later reader does not re-litigate settled facts:

| Step                   | Outcome                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 1. Resource provider   | Confirmed registered.                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| 2. Account + region    | Confirmed: account `reboundman-signing`, region **West US** → endpoint `https://wus.codesigning.azure.net` (`wus`, not `wus2`/`wus3`).                                                                                                                                                                                                                                                                                                                                               |
| 3. Identity validation | **Confirmed** **`Completed`** — closes Open question 1.                                                                                                                                                                                                                                                                                                                                                                                                                              |
| 4. Certificate profile | **`WordMD-PublicTrustCert`, type Public Trust, status Active**, subject `CN=ReboundMan.com LLC, O=ReboundMan.com LLC, L=Sheridan, S=…`, expiry 8/14/2026 — closes Open question 2. Program type left **None** (correct: change only for the Windows endpoint security platform program) and both **Include street address** / **Include postal code** left unchecked, keeping the business street address out of a publicly distributed certificate at no cost to SmartScreen trust. |
| 5. Signer role         | Assigned to JJ's user at **account scope via the portal**, deliberately (single-owner account) — see the Behavior step 5 decision and the second-profile caveat there.                                                                                                                                                                                                                                                                                                               |

Part B followed the same session (steps 6–8, all confirmed):

| Step                  | Outcome                                                                                                                                                                                                                                                                    |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 6. Client tools       | Installed via `winget`, publisher verified as Microsoft, version **0.1.128**, installer hash verified by winget. Real install paths discovered and recorded in step 6 (they are not under `%ProgramFiles%\...\ArtifactSigning`, and the MSI records no `InstallLocation`). |
| 7. Build-machine auth | **`az login`** **as JJ**, service principal deferred — closes Open question 3; see step 7 for what that accepts and the triggers to revisit.                                                                                                                               |
| 8. `metadata.json`    | Written to `C:\Code\Signing\.wordmd-signing\metadata.json`, outside all git repos and outside OneDrive.                                                                                                                                                                    |

Note on the certificate expiry date: 8/14/2026 is the *certificate profile's* renewal horizon, not the 3-day validity of an individual issued signing certificate. The `/tr` timestamp requirement in step 9 is what keeps already-signed binaries valid past that 3-day window; it is not optional.

**Part C (steps 9–12) is all that remains**: the manual test sign, the sign-before-package ordering, the `$q`-escaped `SignTool=` wiring in `WordMD.iss`, and the `DEPLOY.md` checklist update. Step 9's command is paste-ready with JJ's real paths.

**1. Is identity validation actually** **`Completed`** **on the existing account?** — **CLOSED 2026-08-11: yes,** **`Completed`.** Original discovery and options retained below for history.

Discovery: this is the one step in the walkthrough (Behavior, step 3) Emily cannot verify without portal access — JJ said he "has" the account, not that validation finished, and Microsoft's own docs state this can take 1–20 business days. Everything from step 6 onward produces real signatures only if this is done.

Options:

* **(a) JJ checks the portal now** (Artifact Signing account → Overview or Objects → Identity validations) and reports back `Completed` or not, before spending time on Parts B/C. Cost: two minutes. Risk: none.

* **(b) Proceed with Parts B/C regardless and let a failed signing attempt (Behavior step 9) reveal the problem.** Cost: same total work either way, but a failure at step 9 is less obviously "identity validation isn't done" than a status the portal states plainly.

**Emily's recommendation: (a).** A two-minute portal check now is cheaper than debugging a signing failure later without knowing which of several possible causes (region mismatch, wrong role, incomplete validation) produced it.

**2. Public Trust certificate profile — confirm this is what the existing profile is, not Public Trust Test or Private Trust.** — **CLOSED 2026-08-11: confirmed Public Trust, Active.** Original discovery and options retained below for history.

Discovery: Behavior step 4 names Public Trust as the profile type that actually clears SmartScreen for public distribution; Public Trust Test is explicitly for testing (doesn't carry public trust) and Private Trust is for internal/enterprise distribution, not a public GitHub release. If JJ's existing certificate profile was created as one of the other two — plausible if set up for an earlier, different purpose — Behavior steps 6-12 would all complete successfully and still not remove the SmartScreen warning, a confusing, silent failure of the actual goal.

Options:

* **(a) JJ confirms the existing profile's type** (Objects → Certificate profiles → the profile → its type field) before proceeding, or creates a new Public Trust profile if the existing one is wrong. Cost: two minutes to check; profile creation (if needed) is Behavior step 4, already in the walkthrough. Risk: none.

* **(b) Proceed and discover the mismatch only if SmartScreen still warns after everything else works.** Cost: the same, but discovered only after the full walkthrough — the most expensive place to find a wrong assumption.

**Emily's recommendation: (a).** Same shape as question 1 — cheap to check now, expensive to discover as the very last, most confusing possible failure mode.

**3.** **`az login`** **(JJ's own full Azure session) or a dedicated service principal scoped to just the Signer role?** — **CLOSED 2026-08-11: (b),** **`az login`, chosen consciously.** JJ: "this seems like overkill for the two small apps I have. If we grow to a larger company, I'll adapt then." Both halves are now settled: the user identity holds account-scope Signer (step 5) and the build machine authenticates as JJ himself (step 7). Behavior step 7 records what that accepts — the cached session carries subscription-level Owner — and names the concrete triggers (a second signer, CI-based signing, or a subscription holding anything else of value) that should turn the deferred service principal into real work. This is the informed-choice outcome the option below anticipated, not a bypass of it.

Discovery: Behavior step 7 defaults to a scoped service principal after round-1 review (Hawk, High) flagged interactive `az login` as putting JJ's full personal Azure access on a general-purpose build machine for a task that needs one narrow role. This is a real setup-cost-vs-blast-radius tradeoff, not a fact Emily can resolve alone — it depends on how much JJ trusts the specific machine he builds releases on, and whether extra one-time setup (creating and securing a service principal's credentials) is worth it for a single-developer project.

Options:

* **(a) Service principal, scoped to just the Signer role** (Behavior step 7 as currently written). Cost: one extra one-time setup (`az ad sp create-for-rbac` plus a role assignment), and a service-principal secret or cert to store securely on the build machine instead of relying on an interactive browser login. Benefit: a compromised build machine can only sign WordMD releases, nothing else in JJ's Azure account. Reversible: yes, the service principal can be deleted and access revoked independent of JJ's own account.

* **(b) JJ's own** **`az login`.** Cost: zero extra setup. Risk: the build machine's cached session carries whatever broader access JJ's own Azure identity has — unknown from here, but for a personal/admin account this is often broad (subscription-level access, other resources, billing). Reversible: yes (revoke the session), but the exposure window exists for as long as the broader session is live on that machine.

**Emily's recommendation: (a), already reflected in Behavior step 7.** The setup cost is one-time and modest; the risk (a compromised build machine inheriting JJ's full Azure access) is open-ended and hard to bound in advance. If JJ judges the build machine trustworthy enough that this is unnecessary caution, (b) is a legitimate, informed choice — but it should be chosen, not defaulted to.
