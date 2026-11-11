---
spec: 10258
feature: opt-in-automatic-update-check
status: draft
agent: emily
drafted: 2026-11-11
source: spec/punchlist.md (Ideas, 2026-11-08), from features/check-for-updates.md "Later"
size: M
priority: L
---

# Opt-in automatic update check

**ReboundMan-WordMD** · spec **10258** · `opt-in-automatic-update-check-off-by-default-help-menu-toggl`

Spec Review Fleet (round 1, 2026-11-11): Sage SHIP WITH FIXES + Hawk SHIP WITH FIXES, all ten applied (the false "no scheduler exists" claim corrected against the autosave timer and the update feature's own startup work; dialog single-flight hoisted and sequenced with crash recovery and the previous-update notice; the attempt marker consulted; dev builds skip silently; LastAutoUpdateCheckUtc stamped at attempt start with Save(), future-clamped, shared with manual checks, and carried through SettingsStore.Load()'s explicit copy; HELP.md:94 corrected in place with the honest IP-and-User-Agent daily-beacon wording; Close stays the default button on the unprompted dialog).

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
(M)(L) Opt-in automatic update check, noted 2026-11-08. Needs a decision, because it makes WordMD contact GitHub without a click. See "Later" in `features/check-for-updates.md`. (Silent update with automatic relaunch is built; see Done.)
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md` Ideas, noted 2026-11-08 when the Check for Updates build closed; the design intent is written in `spec/features/check-for-updates.md:174-176` ("Later": opt-in, off by default, a Help-menu toggle in the style of the telemetry toggle, a once-a-day cap) and the deferral reason at `check-for-updates.md:41`: the app had never made a network request of its own, so the first one was deliberately made a user click.

## Problem

Update checking today is entirely manual: a person who never opens Help > Check for Updates never learns a release shipped. The built feature deliberately stopped there to protect the documented local-only privacy stance. The missing half is the usual one: an automatic check for people who choose it, with the same privacy posture (off until asked for, honest about what it contacts).

## Model (what the code has today, cited)

- The whole check pipeline exists and is UI-independent: `UpdateService.CheckAsync(Version, CancellationToken)` (`src/WordMD/UpdateService.cs:508`) calls the pinned GitHub endpoint (`LatestReleaseApi`, `UpdateService.cs:128`, host-allowlisted at `:154`) and returns `UpToDate | Available | Failed` (`UpdateCheckStatus`, `:15`). Draft and prerelease releases are never offered (`:581-582`). Failures come back as human text with a code (`:518-547`).
- The manual entry point is `MenuCheckForUpdates_Click` (`src/WordMD/MainWindow.Updates.cs:24`), which drives the dialog flow, including the silent-update path that is already built.
- The policy off-switch exists: `UpdateService.IsDisabledByPolicy()` reads `DisableUpdateCheck` under `HKLM\Software\Policies\WordMD` (`UpdateService.cs:314-326`) and the menu item is hidden entirely when set (`MainWindow.xaml.cs:72-73`). Any automatic check must sit behind the same gate.
- The opt-in pattern to copy is telemetry: `SettingsStore` persists `TelemetryOptIn` and `TelemetryPromptShown` booleans in the app's JSON settings (`src/WordMD/SettingsStore.cs:15-16`), surfaced as a Help-menu toggle. Note `SettingsStore.Load()` copies each field explicitly (`SettingsStore.cs:44-45`), so a new field that skips that copy silently never loads; the build adds `AutoUpdateCheck` and `LastAutoUpdateCheckUtc` to the copy block.
- Startup machinery that already exists and must be sequenced with, corrected round 1 (the draft claimed none existed): an autosave `System.Threading.Timer` (`MainWindow.xaml.cs:36,104,1814`), and the update feature's own startup work, `CleanStaleDownloads` (`MainWindow.xaml.cs:106`) and the "last update didn't finish" notice `ShowPreviousUpdateNoticeAsync` fired from the startup lambda (`MainWindow.xaml.cs:274`). What does not exist is any automatic call to `CheckAsync`; the manual click path is the only caller. The `_updateFlowActive` guard is local to the click handler today, so sharing it with a background caller is part of this build, and WinUI's `ContentDialog` throws when two dialogs collide (crash recovery and the previous-update notice are live collision partners at startup).

## Charter

A person who turns on "Check for updates automatically" gets at most one background check a day, sees something only when an update is actually available, and can turn it off where they turned it on. Everyone else's WordMD never contacts GitHub without a click, exactly as today.

## Behavior

1. **The toggle**: a checkable Help-menu item, default off, persisted as `AutoUpdateCheck` in `SettingsStore` beside the telemetry booleans (and added to `Load()`'s explicit copy block). Its text promises only what the schedule can keep: "Check for updates automatically (at most once a day, when WordMD starts)", since a startup-only check cannot honor a bare "once a day" across a long-running session. No first-run prompt: unlike telemetry there is no value to ask for, the menu is discoverable where the manual check already lives.
2. **The schedule**: on startup (and at most once per 24 hours, tracked by a persisted `LastAutoUpdateCheckUtc`), if the toggle is on and policy allows, run `UpdateService.CheckAsync` in the background after the window is up, never blocking startup or file open. `LastAutoUpdateCheckUtc` is written when the attempt starts, with an immediate `Save()`, not when it succeeds, and a stored value in the future is clamped to now (round 1 Hawk and Sage; the attempt-marker precedent at `UpdateService.cs:477`), so a crash or a clock jump can neither tighten the cap into a beacon loop nor silence it forever. A manual Help > Check for Updates also stamps it: the cap's job is at most one GitHub contact a day that the person did not click for, and a fresh manual answer makes a same-day background repeat pure noise.
3. **Quiet by design**: `UpToDate` and `Failed` results show nothing and log nothing visible (the existing debug trace is enough); only `Available` surfaces, reusing the existing update-available flow from the manual path so there is one dialog, not two. Guards ride along from the manual path (round 1 Hawk and Sage): the automatic path applies the same dev-build version check the click handler enforces (`MainWindow.Updates.cs:56-64`), but silently, skipping the check entirely when the running version does not parse as a release (the refusal fires before `CheckAsync` on the manual path, so the automatic path must decide it without a dialog); it shares a hoisted single-flight guard with the manual flow (`_updateFlowActive` is click-local today, so the build promotes it to one shared gate); it consults the "last update didn't finish" attempt marker and does not auto-check while that notice is pending or unacknowledged; and it silently drops its `Available` result whenever another dialog (crash recovery, the previous-update notice, a manual update flow) is showing or pending, because WinUI's `ContentDialog` throws on collision, rather than queueing a second dialog.
4. **Gates**: `IsDisabledByPolicy()` wins over the toggle (and the hidden menu item means the toggle is invisible too, matching `MainWindow.xaml.cs:72-73`); no check while offline beyond what `CheckAsync`'s own failure handling already absorbs.
5. **Honesty surfaces**: HELP.md's existing line "WordMD never checks in the background" (HELP.md:94) is corrected in place, not contradicted by an appended paragraph, and the disclosure uses the honest wording HELP.md already established for the manual check (HELP.md:103): the request exposes the machine's IP address and the app's version in the User-Agent to GitHub, and with the toggle on that happens about once a day, a daily beacon, not "nothing but the request itself". The privacy stance wording gets the same treatment. The toggle's own text says "once a day" so the cap is a promise, not an implementation detail.

## Relationships

- `spec/features/check-for-updates.md` (built): the parent; this spec implements its "Later" bullet (`:174-176`) and must not weaken its round-2 fleet fixes (the process-aware marker, the policy off-switch, the release contract).
- `spec/punchlist.md` Ideas, "Follow-ups from the Check for Updates fleet reviews" (open): shares the installer/silent-update surface; no contradiction, but if its real-installer VM test is still unrun when this builds, the automatic path multiplies how often the silent updater runs, which raises that test's priority. Flagged, not resolved.
- The telemetry opt-in (`SettingsStore.cs:15-16`): the pattern this copies; keeping the two toggles visually and behaviorally parallel is part of the ask.

## Permissions

N/A for new grants: the capability runs inside the user's own interactive session and contacts the same single pinned host the manual check already uses (`api.github.com`, `UpdateService.cs:128,154`). The existing `HKLM\Software\Policies\WordMD\DisableUpdateCheck` policy continues to disable both paths; the build ships the settings field, the scheduler, the toggle, and the HELP.md/privacy wording. Nothing needs JJ's hand at deploy.

## Acceptance

- Fresh install: no network call ever happens without a click (verified by the test harness's canned-transport hooks or a network trace note).
- Toggle on: a check runs on next startup, then not again within 24 hours across restarts (a manual check also resets the clock); `Available` shows the same dialog the manual path shows, with Close as the default button so a bare Enter on an unprompted dialog never starts a download; `UpToDate`/`Failed` show nothing.
- With crash recovery or the previous-update notice on screen at startup, the automatic check either waits or drops silently; no dialog collision, no second queued dialog. A dev (non-release) version never checks and never shows anything.
- Toggle off, or policy set: no automatic call, and with policy set the toggle is not visible.
- HELP.md and the privacy wording name the behavior and the host.
- The punchlist's decision framing is honored: the spec reaches JJ's gate as the decision artifact; building it is his verdict, not a default.

## Open questions

1. **Does opt-in automatic checking fit the privacy stance at all: ship it (a), or record a deliberate "manual only" rejection (b)?**
   - **Discovery**: the punchlist line itself says "Needs a decision, because it makes WordMD contact GitHub without a click." The deferral note (`check-for-updates.md:41`) was written when the app had never made any network request; that line has since been crossed by the user-initiated check, but automatic contact is still a stance change: a GitHub API call exposes the caller's IP to GitHub on a schedule rather than on demand. Everything else in this spec is mechanics around this one call.
   - **Options**: (a) ships a strictly opt-in, once-a-day, policy-gated check; cost is one more stance sentence in HELP.md; reversal is removing a menu item. (b) costs nothing now and keeps the stance maximally simple; reversal is this spec again later; the price is users on old builds staying there silently.
   - **Recommendation**: (a). Off by default preserves the stance for everyone who does not act, and the people who flip the toggle are asking for exactly this contact. The honest wording in Behavior 5 is what keeps the stance true rather than merely technically defensible.
