---
feature: check-for-updates
status: built
agent: claude
drafted: 2026-11-08
source: JJ direct ask, 2026-11-08: "Can you add a 'check for updates' option to the app, and propose a way to implement that with finding and installing the latest release from Github if there is one available"
size: M
priority: M
---

# Check for Updates

Help > **Check for Updates...** finds the newest WordMD release on GitHub and, if it is newer than the running copy, downloads it, verifies it, and hands off to the normal installer.

## The proposal

WordMD already ships the way an updater wants: one signed Inno Setup installer per release, attached to a GitHub Release next to its SHA-256, built by a script that asserts the signer identity. So the updater needs no server, no new packaging format, and no change to the installer. It reads the release the project already publishes.

1. **Find.** `GET https://api.github.com/repos/ReboundMan/ReboundMan-WordMD/releases/latest` (public, unauthenticated). That endpoint returns the release GitHub shows as Latest, never a draft or pre-release. If its tag, read as strict `MAJOR.MINOR.PATCH`, is greater than the running version, offer it. Equal or older (including a rollback that marked an older release Latest) is "up to date". The updater never moves anyone backwards. A running build whose own version is not a plain `X.Y.Z` (a development build such as `2.2.0-rc1`) refuses to compare rather than guess.
2. **Offer.** A dialog shows the new and running versions, what will happen, and the release notes (plain text, in a read-only box that the keyboard and screen readers can reach). Buttons: Download and install, View release notes, Not now. **Only the installed copy can install.** The running folder must equal the install location in Inno's uninstall registry key for this app. A portable or development copy sees an explanation and an "Open releases page" button instead, because it would otherwise run the installer against a different install and then close the copy the user was using.
3. **Download and verify.** Fetch `WordMD-Setup-<version>.exe` and its `.sha256` asset into a fresh per-attempt folder under `%LOCALAPPDATA%\WordMD\updates\`. Every check below is required before anything is run:
   - the size equals what the API reported;
   - the SHA-256 equals the hash in the release's `.sha256` asset;
   - the Authenticode signature is valid (WinVerifyTrust, timestamp honored) **and** the signer is on the accepted list (see "Trust model");
   - the installer's own signed `ProductName` and `ProductVersion` match the release that announced it.
4. **Hand off.** If any tab has unsaved changes, the same save/discard/cancel prompts as File > Exit run first (cancelling abandons the update). Then the verified installer is started through the shell in its normal visible window, WordMD waits a moment to see that it stayed alive, and exits. The installer's last page offers "Launch WordMD", checked by default, which brings the app back.

It is **manual only**. There is no background polling, no check at startup, no timer. Every request starts from the user clicking the menu item.

## Why this way, and what I rejected

| Option | Verdict |
| --- | --- |
| Menu item that just opens the releases page | Safe and trivial, but it is not "finding and installing". Kept as the fallback button on every error and for copies that cannot self-update. |
| **Check + verified download + visible installer (built)** | Smallest thing that is real, needs no installer change, and every failure is visible to the user. |
| Silent install (`/SILENT`) with automatic relaunch | Better experience, deferred. `installer\WordMD.iss` has `skipifsilent` on the launch entry, so a silent update would not relaunch the app, and a silent failure would leave the user with a vanished app. Fixing it means adding a `[Code]` section to the installer, and CLAUDE.md lists the installer script as ask-first. See "Later". |
| Automatic check at startup, or on a timer | Deferred and, if ever built, opt-in. The app had never made a network request of its own and has a documented local-only privacy stance; this feature is the first, so it is the user's explicit action. |
| Let Setup close and restart the running app itself (Restart Manager) instead of WordMD exiting | Worth prototyping later. It would keep the app alive if the user cancels the installer, but Restart Manager would close the window without the unsaved-changes gate. |
| winget / MSIX with App Installer | OS-managed updates, but a different distribution channel and signing story, and a bigger decision than a menu item. Not rejected forever. |
| A third-party updater framework (Velopack, NetSparkle) | Adds a dependency and a second packaging format to solve a problem the existing installer already solves. Revisit if silent, delta, or channel updates become real requirements. |
| A signed `latest.json` published with each release | Would close more of the coupling to release shape and avoid the rate-limited API. Not needed to close the rollback gap any more (see Trust model), so deferred. |

## Components

- `src/WordMD/UpdateService.cs`. All networking, parsing, download, and verification. No UI types, so it was tested on its own (see Verified).
- `src/WordMD/MainWindow.Updates.cs`. The dialog flow only. A partial class so `MainWindow.xaml.cs` did not grow further.
- `MainWindow.xaml.cs`. `MenuExit_Click`'s "walk every unsaved tab" loop was extracted, unchanged, into `ConfirmAllDirtyTabsAsync()` so Exit and Update share one implementation, and the constructor starts a best-effort cleanup of old downloads (local deletion only).
- `SupportLinks.ReleasesPage`, the menu item in `MainWindow.xaml`.

## Trust model

What each layer buys. The honest summary is that **the signature is the anchor and everything else narrows what a signed file can do**.

1. **TLS to GitHub, exact hosts only.** Every request and every redirect hop is checked before it is sent: HTTPS, default port, no userinfo, and a host that is exactly `api.github.com`, `github.com`, or one of GitHub's three release-asset hosts. Not a `*.githubusercontent.com` wildcard, which would also admit hosts that serve arbitrary user uploads. Redirects are followed by hand (hop limit 5, no cookies, no credentials sent to GitHub). The asset URLs in the API response must equal this repo's `releases/download/<tag>/<name>` path exactly. The "View release notes" link is built from the validated tag, never taken from the release's own `html_url`.
2. **Authenticode with a pinned signer.** A valid chain and signature, and the signer certificate must match an entry in `UpdateService.AcceptedSigners`: common name `ReboundMan.com LLC`, issuer organization `Microsoft Corporation`, and the Artifact Signing account's identity EKU (`1.3.6.1.4.1.311.97.84717075...`). All four shipped installers match, across two different issuing CAs. The issuing CA's own name is deliberately not pinned because Microsoft rotates those. The certificate checked is the one from the signature WinVerifyTrust validated (tested against a file whose certificate bag had a forged `ReboundMan.com LLC` certificate inserted: still rejected).
3. **Version binding.** After the signature passes, the installer's `ProductName` must be `WordMD` and its `ProductVersion` must equal the announced release. Both live in the PE version resource, which is inside the signed bytes, so they cannot be forged without a signature. This closes the rollback gap: an old, validly signed installer renamed and attached to a newer tag now fails with code `version`. (An earlier draft of this spec said the version was not inside signed metadata. That was wrong; Hawk found it and I confirmed it directly on all four shipped installers.)
4. **Size and SHA-256.** Both come from the same release as the installer, so they catch truncation, corruption, and a CDN serving the wrong object. They do not defend against a malicious release. The signature alone also catches corruption (flipped-byte and truncation tests prove it), so this layer is redundant with it. Kept anyway because it is cheap, fails closed, is already published by every release, and HELP.md promises it. A candidate to drop if it ever causes a false failure.
5. **Verify what you launch, up to process creation.** After download the file is reopened read-only with sharing that denies writers and deleters, the hash is computed from that handle, and the handle is held until the installer has started. A same-user process cannot swap the file between the checks and the launch. **This is narrower than it sounds:** the handle is gone once WordMD exits, and Inno's setup re-reads its own file by path while it runs, so this does not protect the file after launch. The installer's authenticity rests on the signature, not on this handle.

**What this does not protect against.**
- **A compromised signing identity.** Signed malware passes every check. Revocation is not consulted (`WTD_REVOCATION_CHECK_NONE`), a deliberate trade to avoid spurious failures behind proxies and offline endpoints; a Microsoft-side revocation of the signing identity is therefore not honored by already-installed copies. Soft-fail revocation (hard failure only on an explicit "revoked" result) is a possible later hardening.
- **A same-user attacker.** They can already edit a per-user install, so this is largely out of scope. The one case where it matters is a per-machine install, where the user elevates a file that was downloaded to a user-writable folder; the signature on that file is still what protects it.
- **TLS interception** is as powerful as a compromised release and no stronger: the signature checks still gate execution. There is no certificate pinning.
- **Availability.** Anyone who can publish a release (for example a compromised GitHub account) can publish a junk newer tag, and users will see an error instead of an update until it is removed. No code runs.
- The downloaded file carries no Mark-of-the-Web, so SmartScreen does not prompt; the in-app signature check stands in for it.
- Windows itself may contact Microsoft while validating the certificate chain, as it does for any signed program. HELP.md says so.

**Identity rotation.** A legal-name change, a new Artifact Signing profile, or a re-validation that changes the EKU makes installed copies reject new releases, and those copies can only be fixed by a manual install. The accepted signers are therefore a list. To rotate: add the new entry, ship that release still signed by the old identity, and only then switch signing. `spec\DEPLOY.md` ("In-app updater contract") records this.

## Errors

Every user-facing message is a fixed, plain sentence chosen in `UpdateService.cs`, followed by a short code from a fixed vocabulary (`network`, `tls`, `proxy-407`, `timeout`, `rate-limit`, `http-503`, `bad-response`, `bad-tag`, `no-assets`, `asset-path`, `redirect-host`, `size`, `checksum`, `signature-0x...`, `signer`, `version`, `product`, `disk`, `launch`, and similar). Nothing from a remote response, a framework exception message, or a local path is ever shown. This is deliberate: a duplicate key in a hostile release JSON used to put a megabyte of attacker-chosen text into the dialog through an exception message. Each failure also says what it did not do ("Nothing on your computer was changed."), and every error dialog offers the releases page. The distinct exception types separate "the installer failed a check" from "the download was not from where WordMD expects" from an ordinary network or disk failure, so a moved GitHub asset host does not tell users their installer failed a safety check.

## Privacy

This is the first network request WordMD makes on its own. It goes only to GitHub, only when the user clicks the menu item (and, for the download, only after they choose Download and install). GitHub sees the user's IP address and a `User-Agent: WordMD/<version>` header, the same as visiting the releases page. No document content, no file paths, no telemetry, no identifiers. The telemetry and feedback behavior is untouched. `HELP.md` has a user-facing "Updates" section that says this in plain words, including the Windows-contacts-Microsoft caveat.

## Fleet review

Because this is the app's first network call and it executes a downloaded binary, it went through Hawk (security), Chaos (adversarial QA), Sage (architecture), and Beacon (accessibility) before being committed. None found a way to run unverified or wrong-signer bytes, and none found a crash path. They did find real problems, now fixed:

| Finding | From | Disposition |
| --- | --- | --- |
| Rollback: an old signed installer under a newer tag passed every check | Hawk | **Fixed.** Signed `ProductName`/`ProductVersion` must match the announced release. Proved with the real 2.0.0 and 2.1.0 installers. |
| Signer pinned by common name only, as one constant, with no rotation path | Hawk, Sage | **Fixed.** A list of pins (name, issuer organization, account EKU). The build script's looser substring match is now described accurately in the code. |
| Running copy never checked that it is the installed copy | Sage | **Fixed.** Installed-copy gate via Inno's uninstall registry key. |
| Network timeout during download shown as a silent "user cancelled" | Chaos | **Fixed.** Cancel is decided by the user's actual click; service timeouts are real errors. |
| Cancel at the instant the work finishes was ignored, the update proceeded | Chaos | **Fixed.** Decided from the Cancel click and from what closed the dialog, not from task state afterward. |
| `ShowAsync` throwing leaked a running download | Chaos | **Fixed.** The task is cancelled and any late result discarded. |
| Hostile `html_url` (`/releases/../..`) could open an attacker's repo; hostile error text via exception messages | Hawk, Chaos | **Fixed.** URL built from the validated tag; all messages are fixed strings. |
| Release notes unreachable by keyboard and screen readers | Beacon | **Fixed.** Read-only text box, named; confirmed exposed as an Edit control in the live UI. |
| Progress bar unnamed, no text readout, bar sat at 100% during checks, dialog flashed on fast checks, secondary text risked low contrast, the key warning was in 12 px grey | Beacon | **Fixed.** Named bar, live-region percentage, indeterminate while checking, dialog only if work outlasts 400 ms, explicit dialog theme, warning at normal size. |
| Raw exception/HTTP/hex text in dialogs | Beacon, Chaos, Hawk | **Fixed.** See "Errors". |
| Overclaimed comments and docs ("verified bytes are the bytes launched", "Launch WordMD brings the app back", "mirrors the build script", Rollback section) | Sage | **Fixed.** Wording corrected here, in the code, and in `DEPLOY.md`, whose Rollback section is now roll-forward. |
| No proxy credentials (authenticating proxies failed); no header timeout or wall-clock cap; cookies on; broad host suffix; `$` instead of `\z`; BOM literal; running version with a suffix parsing as 0.0.0; release-notes control/bidi characters; cleanup on the UI thread and only on the next check; default button started the download | Chaos, Hawk, Sage | **Fixed.** |
| Installer exiting early left no app and no installer | Sage | **Partly fixed.** WordMD now waits briefly and does not exit if Setup died immediately with an error. If the user later cancels Setup or declines an elevation prompt, the app is still gone; documented in HELP.md and "Known limits". |
| Redundant `.sha256` layer | Sage | **Declined for now**, reasons under Trust model item 4. |
| Soft-fail revocation | Hawk | **Deferred.** |
| CI enforcement of the release contract; a policy off-switch; a shared `ContentDialog` gate for the whole app; save-conflict returning "saved" while the tab is still dirty (also affects File > Exit); the startup file-open path lacking a try/catch; the csproj defaulting `PublishTrimmed` on; a Store-build guard | Sage, Chaos | **Follow-ups**, tracked in `spec\punchlist.md`. Several are pre-existing and out of scope here, and editing CI is ask-first. |

One reviewer claim turned out to be partly wrong: Sage said the installer carries no version metadata (it reported 0.0.0.0). `FileVersion` is blank, but `ProductVersion` and `ProductName` are set; that is what made the rollback fix possible without touching `installer\WordMD.iss`.

## Verified

Service logic, with a throwaway harness that compiled `UpdateService.cs` directly (kept outside the repo; the repo still has no test project): **118 checks, all passing**, plus four fake-proxy scenarios run as separate processes.

- Version parsing; `.sha256` parsing; the exact-host allowlist (including `raw.`/`gist.`/`avatars.githubusercontent.com`, look-alike and trailing-dot hosts, ports, userinfo); release-JSON handling (newer, same, older, draft, pre-release, bad tag, missing/renamed/not-uploaded assets, foreign host/repo/tag, size extremes, wrong shapes, duplicate keys); release-notes sanitizing (control and bidi characters, surrogate-safe truncation, a 2 MB line).
- **Live against GitHub:** the real check, and the real 66 MB download and verification of v2.1.0 through GitHub's redirect to its asset host. The held handle blocks delete and write between verification and launch.
- **Signer pin on all four shipped installers** (two issuing CAs, certificates that expired long ago, so timestamps are honored), and each pin field is load-bearing (wrong name, issuer, or EKU each fails).
- **Rollback binding with real signed files:** the 2.0.0 installer announced as 2.1.0, and the genuine signed 2.1.0 bytes announced as v2.1.1, are both refused after the signature itself passes.
- Rejections on real files: the unsigned build, one flipped byte, a cut-off tail, plain text renamed `.exe`, and a validly Microsoft-signed binary (wrong signer, and wrong product).
- Negative downloads with nothing left behind: checksum mismatch, a checksum file naming another installer, size mismatch, foreign URL, path-traversal and trailing-newline file names, cancel mid-download.
- Installed-copy detection against this machine's real registry entry (match, trailing slash and case, a sibling folder with the same prefix, no registration).
- Network failures via a fake local proxy: 407, dropped connection, non-TLS bytes after CONNECT, and a stalled connection each give a plain message and the right code, with no proxy address or hex in the text.
- The same unsigned release chain CI runs (`build-signed-release.ps1 -SkipSigning`) builds and packages the final code.

UI, driving isolated copies of the app through UI Automation (a separate single-instance key and a redirected profile, so they shared nothing with the copy of WordMD running on the machine, and a throwaway edit that bypassed the installed-copy gate in one test copy only, reverted afterward): the menu item opens; the install-capable offer dialog, with the release notes exposed as a named Edit control; Not now; the not-the-installed-copy variant (no install button, explains why); the dev-build-version dialog; the "newer than the latest release" and "up to date" dialogs; Download and install showing the progress dialog with a named bar and the live readout going from a percentage to "Checking the download..."; **Cancel mid-download** (dialog closes, app keeps running, nothing left on disk); and the full hand-off, which downloaded, verified, launched the real installer from the verified file, and exited the app (the installer was then closed on its first page each time, nothing was installed).

## Not verified

- **The network-failure, "verification failed", and "couldn't start the installer" dialogs have not been shown in a live window.** The service produces their text and codes (tested), and they render through the same helper as the dialogs that were seen, but the dialogs themselves were not.
- **The unsaved-changes gate during an update.** It is the `ConfirmAllDirtyTabsAsync` that File > Exit now also uses, unchanged, but no update was run with a dirty tab.
- **The installed-copy gate's positive path in a live window** (the real install taking the install branch). The registry read and path match were tested against this machine's real entry, and the negative path was seen live, but the live install path used a test copy with the gate bypassed.
- **A real screen reader, high-contrast themes, and a mixed app/Windows theme.** The accessibility fixes were verified through the UI Automation tree only (names, control types, live text), not by listening with Narrator or by looking at contrast.
- Per-machine (Program Files) installs, ARM64, and an end-to-end update from a real older release through the real installer to completion (every installer launched in testing was cancelled on its first page, so nothing on the machine changed).
- Focus placement after the flow ends, an update started with the network off, and whether Windows' certificate-chain validation makes network requests (so "only GitHub" in the strictest sense is documented, not traced).
- No automated test project exists in the repo. The harness lives in a scratch folder, not in source control.

## Known limits

- The installer opens in its normal window, so an update takes a few clicks. If the user cancels it or declines an elevation prompt, WordMD has already closed and stays closed until started again.
- A per-machine install updates through the same installer; the installer, not WordMD, handles elevation. Security software or policy can block a program launched from `%LOCALAPPDATA%`; the failure dialog shows where the verified file is so the user can run it themselves.
- Release notes are plain text (Markdown syntax appears literally), cleaned of control and bidi characters, and cut to 1,500 characters.
- A release whose installer is not attached yet shows "try again shortly" rather than an update.
- Repo owner, name, and asset-host names are constants; a repository transfer or a move of GitHub's asset host needs a code change (the flow then fails clearly, with the releases page as the fallback).

## Later

- **Opt-in automatic check**, off by default, as a Help-menu toggle in the style of the telemetry toggle, with a once-a-day cap. Needs a decision, because it makes WordMD contact GitHub without a click.
- **Silent update with automatic relaunch.** Add a small `[Code]` section to `installer\WordMD.iss` that honors a relaunch parameter, run the installer with `/SILENT /NORESTART /CLOSEAPPLICATIONS`, and pick per-user versus visible by the installed-copy registry lookup the updater now does. A relaunch must wait for the old process or the single-instance key to be gone, or it will be redirected into a dying process, and a silent failure needs a log and a "last update failed" notice on the next start. Needs approval because it edits the installer script. Compare with letting Setup close and restart the app itself.
- Soft-fail revocation; enforcing the release contract in `release.yml`; a policy switch to hide the menu item on managed machines.
