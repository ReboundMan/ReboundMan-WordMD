# WordMD Deployment and Release Runbook

## Where it lives

| | |
|---|---|
| Product | WordMD Windows desktop app |
| Distribution | GitHub Releases, installer artifact |
| Installer | Inno Setup, `installer\WordMD.iss` |
| Production URL | `https://github.com/ReboundMan/ReboundMan-WordMD/releases/latest` |
| Railway service | `n/a` |
| Firebase project | `n/a` |
| Runtime auth | None |

## Environment variables

WordMD has no required runtime environment variables. It stores settings, recovery snapshots, telemetry, and feedback in per-user local app data folders.

Build and report tooling may use GitHub-provided environment variables inside GitHub Actions. Those are workflow context, not app runtime configuration.

## Build prerequisites

- .NET 8 SDK.
- Node.js 18 or later for the WebView2 editor bundle.
- Inno Setup 6 for installer generation.
- WebView2 Runtime on target Windows machines.

## Build from source

For a local, **unsigned** build (development, or checking that the chain still packages):

```powershell
.\tools\build-signed-release.ps1 -SkipSigning
```

Output: `dist\WordMD-Setup-<version>-UNSIGNED.exe`. The `-UNSIGNED` suffix is deliberate. Unsigned and signed builds must never share a filename, or an unsigned installer can be uploaded to a release by mistake; only a build from the signed path (below) produces `WordMD-Setup-<version>.exe`. **Never publish an `-UNSIGNED` file.**

The equivalent by hand, if you need the individual steps:

```powershell
cd web; npm ci; npm run build; cd ..
dotnet publish 'src\WordMD\WordMD.csproj' -c Release -r win-x64 -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:PublishTrimmed=false -p:SkipWebBundle=true
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" 'installer\WordMD.iss'
```

`-p:SkipWebBundle=true` matters: the csproj rebuilds the web bundle on every build otherwise, so omitting it builds the bundle twice. The version comes from the repo-root `VERSION` file, which both the installer script and the app read; there is nothing to bump in `installer\WordMD.iss`.

## CI

`.github\workflows\ci.yml` runs on Windows, restores Node and .NET dependencies, builds the web bundle, and builds the WinUI project with the already-built web bundle.

## Release checklist

1. Bump `VERSION`; the installer and app binary both read it at build.
2. Update `CHANGELOG.md`.
3. **Run the signed build:**
   ```powershell
   az login          # only if not already logged in; select the tenant holding the signing account
   .\tools\build-signed-release.ps1
   ```
   Succeeds only when it prints `Signed build complete`, the installer path, and the SHA-256. Any earlier failure is fatal by design; nothing partial is releasable. It refuses to proceed unless **both** the app exe and the installer carry a valid, timestamped signature from the expected identity, so a green run is the signature check.

   What it does and why the order is fixed: it signs `WordMD.exe` **before** Inno Setup packages it, because `[Files]` copies whatever bytes sit in the publish folder when ISCC runs. Signing afterwards yields a validly-signed installer wrapping an unsigned app, which still passes a check on the installer alone. It then builds with Inno's `/DSIGN` flag (the switch that activates the `SignTool=` directive in `WordMD.iss`; without it the build is unsigned and lands under the `-UNSIGNED` filename), then verifies both binaries and writes the `.sha256`.

   If a tool lives somewhere unexpected on your machine, override it rather than editing the script; it discovers `signtool`, `ISCC`, and `az` automatically but takes explicit paths:
   ```powershell
   .\tools\build-signed-release.ps1 -SignTool 'C:\...\signtool.exe' -Iscc 'C:\...\ISCC.exe' -Metadata 'C:\...\metadata.json'
   ```
4. Smoke test the generated installer on a clean Windows profile when practical, including both signature checks below.
5. **Release it draft-first**, so `releases/latest` is never briefly assetless (the script prints these three commands with the values filled in):
   ```powershell
   gh release create v<version> --draft --title "WordMD v<version>" --notes-file <notes.md>
   gh release upload v<version> "dist\WordMD-Setup-<version>.exe" "dist\WordMD-Setup-<version>.exe.sha256" --clobber
   gh release edit v<version> --draft=false --latest
   ```
   The tag must be exactly `v` + the contents of `VERSION` (so `VERSION` of `2.0.0` needs tag `v2.0.0`, not `v2.0`); the validation workflow fails the check otherwise. The release *title* is free text.

   That `Release build validation` workflow runs on publish. It confirms the tag matches `VERSION`, re-runs this same script with `-SkipSigning` to prove the chain still builds, and warns if no signed installer is attached. It deliberately attaches nothing itself: CI holds no signing credential, so it could only ever produce an unsigned installer. The shipped artifact always comes from step 3.

### In-app updater contract

Help > Check for Updates (`spec\features\check-for-updates.md`) finds and installs releases with no server of its own, so it depends on each release being shaped exactly like this. A release that deviates is never unsafe; it is just never offered (or fails with a message), which makes the mistake easy to miss. Keep all of these:

- The tag is `v` plus the contents of `VERSION`, strictly `MAJOR.MINOR.PATCH`. No `-rc1` suffix, no fourth part.
- The release is published, not a draft, not marked pre-release, and is the one GitHub shows as **Latest** (the updater reads `releases/latest`).
- It carries two assets with exactly these names, uploaded before publishing (the draft-first order above already does this): `WordMD-Setup-<version>.exe` and `WordMD-Setup-<version>.exe.sha256`. `-UNSIGNED` files are never matched.
- The `.sha256` is `<64 hex>  <installer file name>`, which is what `build-signed-release.ps1` writes.
- The installer has a valid, timestamped signature from an entry in `UpdateService.AcceptedSigners`: the common name `ReboundMan.com LLC`, issued by `O=Microsoft Corporation`, carrying the Artifact Signing account's identity EKU (`1.3.6.1.4.1.311.97.84717075.176126319.764670622.315378995`). The issuing CA's own name is not pinned, because Microsoft rotates those. **If the signing identity, the legal name, or the Artifact Signing identity validation ever changes, copies already installed will refuse updates signed under the new identity.** To rotate safely, add the new entry to `AcceptedSigners` and ship that release still signed by the old identity; only after it is out should signing switch.
- The installer's own `ProductName` (`WordMD`) and `ProductVersion` (set by Inno from `AppVersion`, so from `VERSION`) must equal the announced release. The updater reads them from the signed file and refuses a mismatch, which is what stops an older signed installer from being passed off as a newer release. Don't set `VersionInfoProductVersion` or `AppVersion` in `WordMD.iss` to anything but the `VERSION` file's contents.
- `AppId` in `WordMD.iss` is also how the updater recognizes the installed copy (it reads Inno's uninstall registry key for it). It has to stay constant anyway for in-place upgrades to work.
- Marking an older release as Latest makes the updater tell anyone already on a newer version that they are up to date. It never moves anyone backwards. See Rollback below.
- The updater only follows redirects to GitHub's release-asset hosts (`objects.githubusercontent.com`, `release-assets.githubusercontent.com`, `github-releases.githubusercontent.com`). If GitHub moves downloads to a new host, users see a "redirect-host" error with the releases page as the fallback until `AllowedHosts` in `UpdateService.cs` is updated.

### Code signing

Releases are signed with Azure Artifact Signing under a Public Trust certificate profile, so the published identity reads `ReboundMan.com LLC`. The full setup walkthrough, the specific account and profile values, and the traps hit while getting there live in `spec\features\decide-code-signing-path-for-the-windows-installer.md` (spec 10024).

Signing is local-only by decision, so no credential lives in GitHub Actions. Setting up a build machine takes three things:

1. **Artifact Signing Client Tools** — `winget install -e --id Microsoft.Azure.ArtifactSigningClientTools`. Installs the dlib (under `%LOCALAPPDATA%\Microsoft\MicrosoftArtifactSigningClientTools\`) and a compatible SignTool (into the Windows Kits SDK). The build script finds both.
2. **Azure CLI, logged in** — `winget install -e --id Microsoft.AzureCLI`, then `az login`, **selecting the tenant that holds the signing account**. A bare login can land on a personal-account consumer tenant and fail confusingly; spec 10024 step 7 has the details.
3. **A local `metadata.json`**, naming which Azure resource to sign against. Defaults to `C:\Code\Signing\.wordmd-signing\metadata.json`; override with `-Metadata`. Keep it outside every git repo and outside OneDrive: it holds no secret (the Azure RBAC role is the real access control), but there is no reason to publish a target list. Contents, with values from spec 10024:
   ```json
   {
     "Endpoint": "https://<region-code>.codesigning.azure.net",
     "CodeSigningAccountName": "<artifact signing account name>",
     "CertificateProfileName": "<certificate profile name>"
   }
   ```
   The `Endpoint` region code must match the account's actual region exactly. A mismatch surfaces as a bare **403**, not a useful message.

Artifact Signing certificates are valid for only **three days**, so the timestamp (`/tr http://timestamp.acs.microsoft.com`) is not optional: without it the installer would stop validating three days after signing. The script always passes it and refuses to accept an untimestamped signature.

## Smoke test

- [ ] Confirm the **installer's** signature: right-click `dist\WordMD-Setup-<version>.exe` → Properties → Digital Signatures, expecting `ReboundMan.com LLC`.
- [ ] Confirm the **installed app's** signature after install: `signtool verify /pa "%LOCALAPPDATA%\Programs\WordMD\WordMD.exe"`. This is a separate check on purpose; verifying only the installer is exactly the gap that lets an unsigned inner exe ship.
- [ ] Confirm SmartScreen no longer shows "Windows protected your PC" on a clean profile.
- [ ] From the **previous** release, run Help > Check for Updates and confirm it offers this one. A release that breaks the in-app updater contract above is otherwise just silently never offered.
- [ ] Install without admin rights.
- [ ] Launch WordMD from the Start menu.
- [ ] Open, edit, and save a Markdown file.
- [ ] Switch Source, Formatted, and Split modes.
- [ ] Toggle Light, Dark, and System themes.
- [ ] Send feedback from Help, then verify a local feedback JSONL file is written.
- [ ] Uninstall from Settings or the Start menu shortcut.

## Rollback

WordMD is distributed by GitHub release. **Roll forward, don't roll back.** Because Help > Check for Updates never moves anyone to an older version, marking the previous release as Latest does nothing for people who have already updated to the bad one (it just tells them they are up to date). Cut a new patch release containing the fix or the revert instead; everyone on the bad build is offered it.

Marking the previous known-good release as Latest, or pointing people at the earlier installer, still helps people who have not updated yet and anyone installing fresh. User settings and recovery files are not removed by uninstall.

## Cost notes

No Railway, Firebase, or hosted runtime cost applies. GitHub Actions and GitHub Releases are the primary infrastructure surfaces.

## Incident playbook

1. Reproduce against the latest installer from GitHub Releases.
2. Check local logs under `%LOCALAPPDATA%\WordMD\`.
3. Review GitHub Issues labeled `feedback`, `bug`, or `performance`.
4. Add confirmed issues to `spec\punchlist.md`.
5. Ship a fixed installer or direct users to the last known-good release.
