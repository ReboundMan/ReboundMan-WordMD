---
spec: 10070
feature: wordmd-feedback-hub-submission
status: draft
agent: emily
drafted: 2026-08-07
source: spec/punchlist.md (Ideas / Backlog), undated line, introduced 2026-06-05 in commit 7f8f2c3
size: S
priority: M
---

# WordMD feedback: submit to the ReboundMan hub, or stay local

**ReboundMan-WordMD** · spec **10070** · `wordmd-feedback-hub-submission`

**Spec Review Fleet, 2026-08-07: Sage REWORK, Hawk REWORK. Both applied.** The panel overturned two of this spec's three recommendations. The headline finding, found independently by both personas: the destination's `/admin` inbox was publicly readable, so every posted feedback message would have landed on the open internet. The first draft cited `Caddyfile:75-78` for the write path and never audited the read path. Sizing is unchanged at S for WordMD's own code, but the spec took on two blocking reboundman.com dependencies it did not have. Findings in `reviews/wordmd-feedback-hub-submission-{sage,hawk}.md`.

**Accuracy pass, 2026-11-08: the headline prerequisite is met; the other one is not.** reboundman.com commit `4d4d03f` (2026-11-06, merged as `c36c607`) put the admin pages behind an owner-only password sign-in enforced in the sidecar itself. `analytics/admin_auth.py` checks the password against a PBKDF2 hash held in the `ADMIN_PW_HASH` environment variable and issues its own HMAC-signed `admin_session` cookie scoped to `Path=/admin`, sharing nothing with the existing Lodge gate. `_admin_gated()` (`analytics/server.py:45-51`) is default-deny for every path beginning `/admin`, with only the sign-in pages and `/admin/finishes` excepted (`ADMIN_OPEN_PATHS`, `:42`); `/admin/finishes` is excepted because it keeps its own Lodge gate, which two people hold the password for. An unset hash lets nobody in. So the inbox is an inbox now, not a publication, and the panel's headline finding is closed.

The second prerequisite, a rate limit on the `@track` matcher that carries `/api/feedback`, is still open and still blocks the build. This pass is a documentation correction only: no WordMD code changed, and nothing here approves the build. Every reboundman.com citation below was re-read against that repo at `570f811` (2026-11-08); the line numbers the panel recorded in August had all drifted, so they now carry the commit they were checked against.

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
- [ ] (S)(M) Evaluate whether in-app feedback should submit to the ReboundMan feedback hub instead of only saving locally and prefilling GitHub Issues.
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md`, Ideas / Backlog, undated line. `git log -S "ReboundMan feedback hub" -- spec/punchlist.md` puts its first appearance in commit `7f8f2c3` (2026-06-05), the same day `spec/FEEDBACK.md` recorded the house feedback policy for this app (`spec/FEEDBACK.md:36`). No vault capture behind it; it arrived with the ProjectPatterns feedback-loop adoption, not from a JJ dictation. The word "evaluate" is the ask: this item requests a decision, and only conditionally a build.

## Problem

WordMD's feedback surface works and goes nowhere aggregatable. A submission is appended to a local JSONL file on the user's own machine and, optionally, opened as a prefilled GitHub issue that the user still has to press submit on. `spec/FEEDBACK.md:18` has "Central feedback hub submission" as the one unchecked box on the app's own surface list, and has since the file was written.

The consequence is the anti-pattern the house design names directly: feedback that "dies in someone's inbox" and cannot be themed across apps (`ProjectPatterns/FEEDBACK-LOOP.md`, section 2 anti-patterns). WordMD's own themes table (`spec/FEEDBACK.md:28-30`) still reads "To be filled | 0" because there is nothing to fill it from.

## Model (what the code has today, cited)

**WordMD's feedback path, end to end:**

- `src/WordMD/FeedbackService.cs:35-60` `AppendLocal()` writes one JSON record per line to `%LOCALAPPDATA%\WordMD\feedback\feedback-YYYYMMDD.jsonl` (`FeedbackService.cs:20-23`). It swallows every exception (`FeedbackService.cs:59`) on the stated rule that feedback never throws. **That catch is synchronous and inside the method**; it protects nothing on a background task, which matters for Behavior 4.
- `src/WordMD/FeedbackService.cs:63-85` `BuildGitHubIssueUrl()` builds a `github.com/ReboundMan/ReboundMan-WordMD/issues/new` prefill with title, body, and `feedback,<category>` labels.
- `src/WordMD/MainWindow.xaml.cs:1207-1280` is the dialog handler; `:1257-1279` is the submit block, which always calls `AppendLocal`, fires a `feedback.submit` telemetry event, and opens the GitHub URL only when the dialog's checkbox is ticked.
- The payload is a `Submission` (`FeedbackService.cs:25-32`): category, title, description, and an opt-in diagnostics dictionary built at `MainWindow.xaml.cs:1282-1301` (app version, OS, arch, mode, theme, zoom, open tab count, and for the active tab its line ending, encoding, and whether it has a file path). **No document content, no file path, and no user identity are collected in the payload.** Note the qualifier: the *stored row* gains an IP-derived identifier, see below.
- **Three surfaces, not two.** `spec/FEEDBACK.md:15-18` lists the Help menu item and `Ctrl+Shift+F1`; the accelerators are at `MainWindow.xaml:141` and `:147`, and the menu handler at `MainWindow.xaml.cs:1205`. Sage found a third: `MainWindow.xaml.cs:332` dispatches a `"feedback"` message from the **WebView2 editor**, so the web layer can open the dialog too. `spec/FEEDBACK.md`'s surface list is incomplete and this spec owes that fix.
- Telemetry (`TelemetryService.cs`) is local JSONL only; it is not an existing network path.

**WordMD has no HTTP client at all.** Searched `src/` for `HttpClient`, `WebClient`, `HttpRequest`, and `System.Net.Http`: zero hits (Sage re-verified adversarially). `FeedbackService.cs` imports `System.Net` only for `WebUtility.UrlEncode` (`:4`, used at `:81-83`). Hub submission would be **WordMD's first outbound network call**.

**The hub the item names does not exist.** `ProjectPatterns/FEEDBACK-LOOP.md:90` specifies storage as a single Firestore project `reboundman-feedback-hub`, with an `onFeedbackCreated` Cloud Function (`:70`) and a separate runtime repo (`:206`). Its phasing table (`:181`) puts Phase 1 provisioning at status **Next**. Searched: no `C:\Code\Personal\*feedback*` directory, no `feedback` row in `AUTH-FAMILIES-JJ.md`. Unbuilt, confirmed.

**What exists instead, and what is wrong with it.** reboundman.com runs `POST /api/feedback` on its analytics sidecar. Every line number in this section is as of reboundman.com `570f811` (2026-11-08). `analytics/server.py` has grown by about a third since the panel read it in August, from 999 lines at `c1678aa` to 1348 at `570f811`, so cite it with a commit or re-check it; the August numbers below survive only where they are labelled as history.

- `analytics/server.py:1305-1318` handles the route; `:234-259` `record_feedback()` inserts into a SQLite `feedback` table (`:125-137`) with columns `ts, day, slug, category, message, path, visitor_id, ip_hash`, plus an optional `email` reply address added 2026-11-06 (`:141-143`) that WordMD has no reason to send.
- It already carries an app `slug` (`:244`, 64 chars), so it is multi-app shaped by construction.
- `reboundman.com/spec/FEEDBACK.md:54-60` frames itself as the interim hub with a stable endpoint-swap contract.
- Caddy proxies the write path at `reboundman.com/Caddyfile:154-157`, the `@track` matcher.

**The read path was public, and that is the finding that reshaped this spec. It is fixed as of 2026-11-06.** What both personas reproduced independently in August, kept because the exposure was real and the history should stay legible:

- `Caddyfile:83-86` proxied `/admin`, `/admin/`, and `/admin/*` with **no `forward_auth` and no `basic_auth`**. Twenty lines on, the `@lodge` block did gate with `forward_auth`, so the omission was visible by contrast, not ambiguous.
- `analytics/server.py:765-769` served `/admin` and `:785-787` served `/admin/data.json` unauthenticated. Only `/admin/finishes` was cookie-gated, and its own comment said so because the others were not.
- `build_summary()` emitted the 50 most recent feedback rows with the **full `message` text** at `:387-395` and `:418-422`.
- Adjacent, out of scope, reported to reboundman.com: `/admin/subscribers.csv` (`:789-804`) was on the same non-gate.

**What it looks like now, verified at `570f811`.** The gate is the sidecar's, not Caddy's:

- `Caddyfile:165-168` is still a bare `reverse_proxy` for `/admin /admin/ /admin/*`, now with a comment saying why: the sidecar denies by default, so Caddy has nothing to add. Worth knowing, because reading the Caddyfile on its own still looks like the August exposure.
- `_admin_gated()` (`analytics/server.py:45-51`) is checked before any route dispatch, at `:980-982` for GET and `:1161-1162` for POST, and compares lower-cased because Caddy's path matcher is case-insensitive. `_deny_admin()` (`:964-973`) redirects a page request to the sign-in with a 302 and answers a `.json`, `.csv` or action request with a bare 401. Neither path returns data.
- The three routes the panel flagged now sit behind that check: `/admin` at `:1003`, `/admin/data.json` at `:1023`, `/admin/subscribers.csv` at `:1027`.
- `build_summary()` still reads the 50 most recent feedback rows with the full `message` text (`:519-527`, `:551-555`) and the dashboard still renders them (`:667-681`). That is unchanged and no longer a problem, because it is behind the gate instead of on the open internet.
- `analytics/test_admin_auth.py` holds about two dozen tests over it, including that the dashboard sends a visitor to the sign-in, that the data and the CSV answer 401, that unknown and miscased `/admin` paths are denied by default, that forged and expired cookies are refused, that the Lodge password and cookie open nothing here, and that with no hash set nobody signs in and old sessions die. Setup is documented in reboundman.com's `reboundman.com DEPLOYMENT.md`, section 11.

So posting feedback to that endpoint no longer publishes it. The panel's actual lesson survives the fix: every privacy argument in the first draft was made about the payload and none about the destination, and the destination was where the problem was.

**Three more destination facts the fix depends on:**

- **No rate limiting, and the process is shared. Re-verified and still true at `570f811`.** Searched `Caddyfile` and all of `analytics/` for `rate_limit` and `ratelimit`: no hits anywhere. The sidecar's only throttles are `_LOGIN_SEM` (`:34`) and `_ADMIN_LOGIN_SEM` (`:37`), concurrency semaphores that bound PBKDF2 password verification for the Lodge and admin sign-ins. They are the only source of the file's two 429 responses (`:1099`, `:1180`), and they limit password checks, not request rate. It is **one `ThreadingHTTPServer`** (`:1335`) with one global `_db_lock` (`:93`, taken by `record_feedback()` at `:253`), and the same process answers the Lodge `forward_auth` check (`Caddyfile:186-192`) and, since 2026-11-06, the admin sign-in too. A flood of the feedback endpoint degrades both gates, not just the feedback table. Note that the admin fix made this half worse rather than better: there is now one more security check sharing the process that an unlimited public write path can saturate.
- **`ip_hash` is stored on every row** (`:238`, `:257`), and `visitor_id` defaults to `anon:<ip_hash>` when no `vid` is sent (`:248`), joinable against the `hits` table (`:219`, `:228-230`). `_hash_ip_ua` (`:202-204`) is an unsalted truncated SHA-256 over `ip|ua|day`. So "no user identity" is true of what WordMD sends and false of what gets stored. The admin gate does not change this; it changes only who can read it.
- **No `Content-Type` check** on the write path (`:1255-1272`), so any web page can cross-origin POST as a simple request with no preflight. The body cap for `/api/feedback` is 8192 bytes, enforced at `:1259-1263` with a 413.

**Contradictions to flag, not resolve:** the house design specifies Firestore plus Cloud Functions; the one shipped intake is Python stdlib plus SQLite. Separately, `FEEDBACK-LOOP.md:224` requires the client to "enqueue locally (IndexedDB) and retry"; Behavior 4 below deliberately does not retry, which is a second divergence from the house design, named here rather than hidden.

## Charter

Answer the "evaluate" honestly first, then build the smallest thing that closes `spec/FEEDBACK.md:18`.

The evaluation's answer, revised after review: **yes, submit, to reboundman.com's `/api/feedback` with `slug: "wordmd"`, but not until two reboundman.com defects are fixed.** They are prerequisites, not parallel work, because both change what "submitting feedback" means:

1. `/admin*` is gated, so the inbox is an inbox rather than a publication. **Met 2026-11-06** in reboundman.com `4d4d03f`: owner-only password sign-in in `analytics/admin_auth.py`, default-deny across `/admin*` through `_admin_gated()`.
2. `/api/feedback` is rate limited at the Caddy layer, so a desktop client cannot become a lever against the auth gates that share its process. **Still open at reboundman.com `570f811`.**

One reboundman.com item remains. It is filed there, and this spec does not build it. WordMD's own change stays S.

Three properties this must not break:

1. **Feedback never throws and never blocks.** The existing rule (`FeedbackService.cs:59`) must be re-established on the async path, where the current synchronous catch does not reach.
2. **Local-first stays true.** The local write happens first and unconditionally. The network is an addition, never a replacement.
3. **The user knows where it goes, accurately.** A desktop editor gaining its first network call is a trust event. The notice must survive being checked against the code, which the first draft's would not have.

## Behavior

1. **Post after the local write**, in the submit block (`MainWindow.xaml.cs:1265`). Order is load-bearing: the durable record exists before anything leaves the machine.
2. **Payload, with the truncation budget computed client-side.** `slug: "wordmd"`, `category` from `Submission.Category`. The server truncates its one `message` column at 2000 characters (`server.py:240`) and still answers 200 `{"ok": true}` (`:1317`), so truncation is silent and **the client must budget before sending, not discover truncation after**. Order within `message`: title, then diagnostics (when opted in), then description, so the free-text description absorbs any overflow rather than the structured diagnostics being silently eaten. Reject or trim client-side above 8192 bytes total (`server.py:1259-1263` returns 413 on `Content-Length`, before reading the body). Citations re-verified at reboundman.com `570f811` (2026-11-08); the first draft's numbers were from `c1678aa` and all four had drifted. One correction of substance while checking them: the route is no longer 200-or-nothing. `record_feedback()` raises on an empty `message` (`:240-242`) and on a malformed `email`, and the route turns that into a 400 (`:1308-1310`). Neither case is reachable for a well-formed WordMD submission, and over-length still returns 200, so the budgeting rule above is unchanged.
3. **Do not overload the `path` column.** `path` already means "page URL" in every other row of that table (`server.py:246`, re-verified at `570f811`, and it is run through `_sanitise_path()` at `:207-213`, which strips a query and fragment and caps at 512 characters). Send a constant identifying the surface, or omit it; do not put the category there. Normalize the category to the vocabulary in `FEEDBACK-LOOP.md:55` rather than shipping a fourth spelling into an unvalidated column, which would recreate the fragmentation anti-pattern (`FEEDBACK-LOOP.md:64`) this spec cites as its own justification.
4. **Fire-and-forget, specified.** A single static `HttpClient` with a client-level timeout of a few seconds and `AllowAutoRedirect = false`; the `try`/`catch` sits **inside** the task body, because a catch around the launch catches nothing; no retry queue and no unsent-item persistence; a `CancellationToken` so app shutdown does not block on it. Behavior on quit-immediately-after-submit is "the post is abandoned," stated rather than discovered.
5. **The lost-post case is accepted explicitly, not hand-waved.** The first draft justified skipping a retry queue by saying "the weekly report reads the JSONL." Sage checked: **no weekly report exists** (`FEEDBACK-LOOP.md:183` puts it in Phase 3), and nothing reads the local JSONL except the Explorer shell-out at `MainWindow.xaml.cs:1303-1308`. So a failed post is a lost signal, full stop. That is an acceptable v1 tradeoff and it is written down as one. If it proves wrong, the fix is a `posted: false` flag on the JSONL record plus a retry on next start, roughly fifteen lines, which is also what `FEEDBACK-LOOP.md:224` asks for.
6. **Transport is pinned.** HTTPS only; the client rejects any configured endpoint that is not `https://` and on an allowed host. No custom certificate-validation callback.
7. **The endpoint is a compile-time default, not a user setting.** `SettingsStore.cs:30`/`:38` reads an unvalidated `settings.json` from LOCALAPPDATA, and Behavior 4 makes failures invisible by design, so a user-writable endpoint is a silent redirection primitive on disk. Compile-time constant plus the Behavior 6 allowlist. Reversibility is bought at DNS (a stable alias that can be re-pointed), not in the binary. See Open question 1's revised pricing.
8. **GitHub prefill is untouched.**
9. **`spec/FEEDBACK.md` updates with the truth**: tick line 18, set `feedback_endpoint` (`:9`) to name the real destination, and **add the WebView2 surface** the current list omits.
10. **Hub-sourced feedback is untrusted content, permanently.** This is a WordMD spec but the rule has to be written where the pipe is opened. `FEEDBACK-LOOP.md:82` and `:133` describe auto-created punchlist items committed via the GitHub API, and `:192` defaults `auto_punchlist` to true. Emily drafts specs from punchlist lines. A public unauthenticated intake feeding that chain is a prompt-injection channel into agent-written specs and commits. Therefore: `auto_punchlist` stays `false` for every app posting to this intake, hub-sourced text is human-triaged before it reaches any punchlist, and any agent reading it wraps it in untrusted-content delimiters exactly as this spec's own "The ask" section does.

## Relationships

- **Blocked by** one remaining reboundman.com item this spec files and does not build: rate limit `@track` at the Caddy layer. At reboundman.com `570f811` (2026-11-08), `Caddyfile:154` still proxies that matcher, which carries `/api/track`, `/api/feedback`, `/api/subscribe`, `/api/unsubscribe`, `/api/healthz` and `/api/italia27/votes`, with no `rate_limit`, and there is no `rate_limit` anywhere in `Caddyfile` or `analytics/`. The sidecar's only two 429s (`analytics/server.py:1099`, `:1180`) are the `_LOGIN_SEM` and `_ADMIN_LOGIN_SEM` PBKDF2 concurrency semaphores, which bound password verification rather than request rate. Tracked in reboundman.com's own `spec/punchlist.md`, under Bugs as of commit `570f811`, where the entry cross-references this spec.
- **No longer blocked by** gating `/admin*`. Resolved 2026-11-06 in reboundman.com `4d4d03f` (merged `c36c607`): `/admin`, `/admin/data.json` and `/admin/subscribers.csv` all answer an unauthenticated request with a non-2xx, `/admin/finishes` keeps its separate Lodge gate because two people hold that password, and the gate fails closed. Both items were pre-existing exposures of the live site that WordMD would have walked into rather than created.
- **Depends on** `reboundman.com/analytics/server.py` and its `feedback` table: WordMD would be the first non-browser client of that endpoint.
- **Contradicts** `ProjectPatterns/FEEDBACK-LOOP.md:90` on storage (Firestore vs SQLite) and `:224` on client retry. Recorded, not resolved; Open question 1 is where storage gets decided.
- **Sequenced behind** `spec/features/decide-code-signing-path-for-the-windows-installer.md`. Sage is right that these are not unrelated: WordMD's first outbound POST from an unsigned binary, against the August 15 install-without-a-security-warning deadline, is a worse SmartScreen and endpoint-security profile than either change alone. Also recorded for whoever builds it: `Package.appxmanifest:49-52` declares no `internetClient` capability, which is harmless only while `WindowsPackageType=None`; an MSIX packaging change would silently break the POST.
- **Connects to** `spec/FEEDBACK.md:18`, the unchecked box this spec exists to tick.
- **Diminishes** nothing. Local JSONL and GitHub prefill both survive intact.

## Acceptance

- Submitting feedback online writes the local JSONL record **and** produces one row in reboundman.com's `feedback` table with `slug = wordmd`.
- Submitting with the network unreachable, or the endpoint returning 500, or the app quit immediately after submit, still writes the local JSONL, shows the user no error, and never blocks or crashes the dialog.
- **Of the data WordMD sends**: no document content, no file path, no file name, and no user identity appear in the payload; posted diagnostics are exactly the existing `CollectDiagnostics()` keys and only when the diagnostics checkbox is ticked. The free-text description is user-authored and is not filtered, except that a client-side scrub replaces any `C:\Users\<name>` style path before sending (`MainWindow.xaml.cs:1277` shows users their own `LogPath` in an error dialog beside the feedback button, so such a path landing in a description is a realistic paste, not a hypothetical).
- A maximum-length submission with diagnostics on arrives with diagnostics intact and only the description truncated, and the client never sends a body the server would 413.
- A configured endpoint that is not HTTPS, or not on the allowed host, is rejected and the post is not attempted.
- The GitHub prefill path behaves identically to today.
- `spec/FEEDBACK.md:18` is ticked, `:9` names the real endpoint, and the WebView2 surface is listed.
- **Prerequisite gate 1, met 2026-11-06, re-confirm at build time**: `/admin` and `/admin/data.json` on reboundman.com return a non-2xx to an unauthenticated request. Satisfied by reboundman.com `4d4d03f`, verified against that repo at `570f811`, and asserted by `analytics/test_admin_auth.py`. Because `analytics/admin_auth.py` fails closed, a missing `ADMIN_PW_HASH` locks the owner out rather than opening the inbox, so this cannot regress quietly through configuration alone.
- **Prerequisite gate 2, still open, and this one still gates the ship**: `/api/feedback` is rate limited. Not satisfied at reboundman.com `570f811`; see Relationships for where it stands and where it is tracked. Do not build the post path until this lands.

## Open questions

**1. Which thing is "the ReboundMan feedback hub": (a) reboundman.com's `/api/feedback` sidecar, promoted to the house intake now that its `/admin` inbox is gated (Recommended, and that condition is satisfied as of 2026-11-06), (b) build `reboundman-feedback-hub` on Firestore as `FEEDBACK-LOOP.md:90` specifies, or (c) neither, WordMD stays local-only?**

Discovery: the house design doc and the one shipped implementation disagree about storage, and neither knows about the other. `FEEDBACK-LOOP.md:181` has the Firestore hub unstarted (no repo, no Firebase project, no auth-family row). The sidecar already stores a per-app `slug` (`server.py:191`) and already documents the endpoint-swap contract (`reboundman.com/spec/FEEDBACK.md:56-60`). **What review changed**: in August the sidecar was not a working hub, it was a working *publisher*, because `Caddyfile:83-86` left `/admin*` open and `server.py:387-395` rendered full message text there. The panel called that a one-line Caddy fix that had to happen first, and it was right that the first draft's claim of "zero server work" for (a) was wrong.

**What changed since, 2026-11-08**: the fix landed, and it was not one line of Caddy. reboundman.com `4d4d03f` (2026-11-06) built an owner-only sign-in inside the sidecar instead: `analytics/admin_auth.py`, a default-deny `_admin_gated()` over every `/admin` path, and `analytics/test_admin_auth.py` to hold it. That is the larger and more durable of the two shapes the panel priced, because a new admin route is now gated without anyone remembering to gate it. So (a)'s blocking objection is answered: the sidecar is an inbox. What still stands between this recommendation and a build is the second prerequisite, the `@track` rate limit, open at `570f811` and tracked in reboundman.com's punchlist. Nothing in this accuracy pass is an approval to build.

Also corrected: (a)'s reversibility was priced as "one endpoint constant." That is the *web widget's* property (`window.RM_FEEDBACK_ENDPOINT` repoints every client on next page load). WordMD is a self-contained WinExe (`WordMD.csproj:16-18`); repointing it means a rebuild, a release, signing, and a user reinstall. Real reversibility for a desktop client is bought at DNS, by pointing the constant at a stable alias.

Priced:
- **(a)** gains a working multi-app inbox for roughly a day of WordMD work plus a rate limit. The gate half of this price has already been paid, by reboundman.com rather than by WordMD. Costs: SQLite on one Railway volume is a single point of loss, and the classifier and digest layers in `FEEDBACK-LOOP.md` sections 3 and 4 still have no home. Reversal is cheap only if the endpoint constant points at an alias, which Behavior 7 requires.
- **(b)** gains the designed end state with Firestore triggers available. Costs a Firebase project, an auth-family row, Cloud Functions, and a repo before WordMD's one-line item can close, and strands the already-live site feedback in a second store meanwhile. Reversal costs a provisioned project you then abandon.
- **(c)** costs nothing and leaves `spec/FEEDBACK.md:18` unchecked forever. Genuinely defensible for a local-first offline editor, and more defensible after review than before it.

Recommendation **(a)**, unchanged, and its gate condition is now satisfied (reboundman.com `4d4d03f`, 2026-11-06). The remaining condition on a build is the `@track` rate limit, not the inbox. The teaching, which the fix confirms rather than retires: the house nearly built its hub by accident, as a site analytics sidecar with a per-app `slug` column. What the accident did not bring along was an access-control model, because an analytics dashboard nobody links to feels private and is not. Adding one afterwards took a deliberate piece of work, not the one-line Caddy edit it looked like from here.

**2. On first submission, does WordMD: (a) show a one-time notice naming the destination, with the send proceeding, (b) notice plus a destination-labelled checkbox in the dialog, defaulting to ON (Recommended), or (c) send silently?**

Discovery: `CollectDiagnostics()` (`MainWindow.xaml.cs:1282-1301`) is genuinely clean, and the diagnostics block is already behind a checkbox (`:1262`), so the exposure is the text the user chose to write. But two review findings move the bar. First, the destination publishes (see Model), so the accurate notice is not "we send this to reboundman.com" but "this becomes publicly readable," which is a materially different sentence to consent to. Second, "takes nothing the user did not type" is false at rest: `server.py:185`, `:194-195`, `:201` store an IP-derived `visitor_id` and `ip_hash`, unsalted and joinable against the `hits` table, so the user contributes an identifier they never typed.

The first draft rejected a checkbox on the grounds that an unchecked box on a rare dialog is a no. That reasoning holds for a box defaulting **off**; it does not apply to one defaulting **on**, which preserves the submission rate while making the destination visible at the moment of sending and reversible in one click.

Priced:
- **(a)** gains simplicity, costs the user any per-submission control over a destination that may be public. Reversible.
- **(b)** gains an accurate, revocable, in-context disclosure for one checkbox and one line of copy, and keeps effectively all submissions. Trivially reversible.
- **(c)** spends the trust of a local-first tool to save a sentence.

Recommendation **(b)**, with copy that names the host, says the inbox is private, and states that an IP-derived identifier is recorded. **Corrected 2026-11-08**: the panel wrote "private only once the gate lands" because at the time the destination published. The gate landed on 2026-11-06, so whoever writes this copy should say the inbox is private rather than hedge it, and should not write the "this becomes publicly readable" sentence the Discovery above asks for. The second half of the panel's reasoning is untouched: `analytics/server.py:238`, `:248` and `:257` still store an IP-derived `visitor_id` and `ip_hash`, unsalted and joinable against the `hits` table, so the user still contributes an identifier they never typed and the copy still has to say so. The teaching: the consent bar is set by what happens to the data, not by how much of it you collect. A clean payload sent to a public page needs more disclosure than a dirty payload sent to a private one.

**3. Sequencing against the two reboundman.com defects: (a) ship WordMD now and file the fixes separately, or (b) block this spec until both land (Recommended)?**

Discovery: the first draft asked a narrower version of this about rate limiting alone and recommended shipping first, on the reasoning that WordMD adds one human-driven caller to an exposure the site already has. Review falsified both halves. The read-path finding means shipping first actively publishes user feedback, which is not a pre-existing exposure WordMD walks past but a new harm WordMD creates by supplying the content. And the rate-limit risk is larger than "the feedback table fills": `server.py:986` is a single `ThreadingHTTPServer` with one global `_db_lock` (`:60`, `:197`) that also answers the Lodge `forward_auth` check (`Caddyfile:106-108`), so saturating `/api/feedback` degrades a paying-attention security gate on a different part of the site. `_LOGIN_SEM` (`:28-30`) shows the author already reasoned about exactly this class of attack, for login only.

Priced:
- **(a)** gains a few days and publishes every submission in the meantime, with no way to unpublish what was already scraped. Irreversible in the only way that matters.
- **(b)** costs one cycle and two small reboundman.com changes, a Caddy `forward_auth` or `basic_auth` block on `/admin*` and a `rate_limit` on `@track`. Both are edits to a file that already contains a working example of each.

Recommendation **(b)**, firmly. The teaching: "this is a pre-existing exposure, not one we create" is a sound argument right up to the moment your change starts feeding the exposure new content. It was the right call for rate limiting in isolation and the wrong one the moment the read path came into view.

**Updated 2026-11-08**: one of the two defects has landed, so this question is now about one. The read-path half of (a)'s price is gone. Shipping today would not publish anything, and the irreversibility argument, which carried most of the weight behind (b), no longer applies. What remains is the rate-limit half, and the answer there is still (b), for the reason the panel gave and which the new admin gate strengthens: the sidecar is one process with one lock that now answers two security gates, so an unlimited public write path is a lever against both. That is a smaller and more ordinary risk than publishing user feedback, and JJ may reasonably price it differently. If he does, the thing to record is that WordMD shipped against a knowingly unlimited endpoint, not that the prerequisite was quietly dropped.
