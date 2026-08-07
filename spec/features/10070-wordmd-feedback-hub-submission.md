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

**Spec Review Fleet, 2026-08-07: Sage REWORK, Hawk REWORK. Both applied.** The panel overturned two of this spec's three recommendations. The headline finding, found independently by both personas: the destination's `/admin` inbox is publicly readable, so every posted feedback message would land on the open internet. The first draft cited `Caddyfile:75-78` for the write path and never audited the read path. Sizing is unchanged at S for WordMD's own code, but the spec now carries two blocking reboundman.com dependencies it did not have. Findings in `reviews/wordmd-feedback-hub-submission-{sage,hawk}.md`.

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

**What exists instead, and what is wrong with it.** reboundman.com runs `POST /api/feedback` on its analytics sidecar:

- `analytics/server.py:958-969` handles the route; `:182-202` `record_feedback()` inserts into a SQLite `feedback` table (`:90-104`) with columns `ts, day, slug, category, message, path, visitor_id, ip_hash`.
- It already carries an app `slug` (`:191`, 64 chars), so it is multi-app shaped by construction.
- `reboundman.com/spec/FEEDBACK.md:54-60` frames itself as the interim hub with a stable endpoint-swap contract.
- Caddy proxies the write path at `reboundman.com/Caddyfile:75-78`.

**The read path is public, and this is the finding that reshapes the spec.** Both personas reproduced it independently:

- `Caddyfile:83-86` proxies `/admin`, `/admin/`, and `/admin/*` with **no `forward_auth` and no `basic_auth`**. Twenty lines later, `Caddyfile:106-108` does gate `@lodge` with `forward_auth`, so the omission is visible by contrast, not ambiguous.
- `analytics/server.py:765-769` serves `/admin` and `:785-787` serves `/admin/data.json` unauthenticated. Only `/admin/finishes` is cookie-gated, and its own comment at `:771-772` says so because the others are not.
- `build_summary()` emits the 50 most recent feedback rows with the **full `message` text** at `:387-395` and `:418-422`.
- Adjacent, out of scope, reported to reboundman.com: `/admin/subscribers.csv` (`:789-804`) is on the same non-gate.

So today, posting feedback to that endpoint publishes it. Every privacy argument in the first draft was made about the payload and none about the destination.

**Three more destination facts the fix depends on:**

- **No rate limiting, and the process is shared.** Searched `analytics/server.py` for `Access-Control`, `Origin`, `rate_limit`, `ratelimit`: nothing. Hawk confirmed Caddy applies none above the sidecar either. The only throttle in the file is `_LOGIN_SEM` (`:28-30`), which the author added for the Lodge login specifically. It is **one `ThreadingHTTPServer`** (`:986`) with one global `_db_lock` (`:60`, `:197`), and the same process serves the Lodge `forward_auth` check (`Caddyfile:106-108`). A flood of the feedback endpoint degrades the Lodge gate, not just the feedback table.
- **`ip_hash` is stored on every row** (`:185`, `:201`), and `visitor_id` defaults to `anon:<ip_hash>` when no `vid` is sent (`:194-195`), joinable against the `hits` table (`:167`, `:172`). `_hash_ip_ua` (`:150-152`) is an unsalted truncated SHA-256 over `ip|ua|day`. So "no user identity" is true of what WordMD sends and false of what gets stored.
- **No `Content-Type` check** (`:920-927`), so any web page can cross-origin POST as a simple request with no preflight.

**Contradictions to flag, not resolve:** the house design specifies Firestore plus Cloud Functions; the one shipped intake is Python stdlib plus SQLite. Separately, `FEEDBACK-LOOP.md:224` requires the client to "enqueue locally (IndexedDB) and retry"; Behavior 4 below deliberately does not retry, which is a second divergence from the house design, named here rather than hidden.

## Charter

Answer the "evaluate" honestly first, then build the smallest thing that closes `spec/FEEDBACK.md:18`.

The evaluation's answer, revised after review: **yes, submit, to reboundman.com's `/api/feedback` with `slug: "wordmd"`, but not until two reboundman.com defects are fixed.** They are prerequisites, not parallel work, because both change what "submitting feedback" means:

1. `/admin*` is gated, so the inbox is an inbox rather than a publication.
2. `/api/feedback` is rate limited at the Caddy layer, so a desktop client cannot become a lever against the Lodge auth gate that shares its process.

Both are reboundman.com items, filed there, and this spec does not build them. WordMD's own change stays S.

Three properties this must not break:

1. **Feedback never throws and never blocks.** The existing rule (`FeedbackService.cs:59`) must be re-established on the async path, where the current synchronous catch does not reach.
2. **Local-first stays true.** The local write happens first and unconditionally. The network is an addition, never a replacement.
3. **The user knows where it goes, accurately.** A desktop editor gaining its first network call is a trust event. The notice must survive being checked against the code, which the first draft's would not have.

## Behavior

1. **Post after the local write**, in the submit block (`MainWindow.xaml.cs:1265`). Order is load-bearing: the durable record exists before anything leaves the machine.
2. **Payload, with the truncation budget computed client-side.** `slug: "wordmd"`, `category` from `Submission.Category`. The server has one `message` column truncated at 2000 characters (`server.py:187`) and returns `{"ok": true}` regardless (`:968`), so **the client must budget before sending, not discover truncation after**. Order within `message`: title, then diagnostics (when opted in), then description, so the free-text description absorbs any overflow rather than the structured diagnostics being silently eaten. Reject or trim client-side above 8192 bytes total (`server.py:914-918` returns 413).
3. **Do not overload the `path` column.** `path` already means "page URL" in every other row of that table (`server.py:193`). Send a constant identifying the surface, or omit it; do not put the category there. Normalize the category to the vocabulary in `FEEDBACK-LOOP.md:55` rather than shipping a fourth spelling into an unvalidated column, which would recreate the fragmentation anti-pattern (`FEEDBACK-LOOP.md:64`) this spec cites as its own justification.
4. **Fire-and-forget, specified.** A single static `HttpClient` with a client-level timeout of a few seconds and `AllowAutoRedirect = false`; the `try`/`catch` sits **inside** the task body, because a catch around the launch catches nothing; no retry queue and no unsent-item persistence; a `CancellationToken` so app shutdown does not block on it. Behavior on quit-immediately-after-submit is "the post is abandoned," stated rather than discovered.
5. **The lost-post case is accepted explicitly, not hand-waved.** The first draft justified skipping a retry queue by saying "the weekly report reads the JSONL." Sage checked: **no weekly report exists** (`FEEDBACK-LOOP.md:183` puts it in Phase 3), and nothing reads the local JSONL except the Explorer shell-out at `MainWindow.xaml.cs:1303-1308`. So a failed post is a lost signal, full stop. That is an acceptable v1 tradeoff and it is written down as one. If it proves wrong, the fix is a `posted: false` flag on the JSONL record plus a retry on next start, roughly fifteen lines, which is also what `FEEDBACK-LOOP.md:224` asks for.
6. **Transport is pinned.** HTTPS only; the client rejects any configured endpoint that is not `https://` and on an allowed host. No custom certificate-validation callback.
7. **The endpoint is a compile-time default, not a user setting.** `SettingsStore.cs:30`/`:38` reads an unvalidated `settings.json` from LOCALAPPDATA, and Behavior 4 makes failures invisible by design, so a user-writable endpoint is a silent redirection primitive on disk. Compile-time constant plus the Behavior 6 allowlist. Reversibility is bought at DNS (a stable alias that can be re-pointed), not in the binary. See Open question 1's revised pricing.
8. **GitHub prefill is untouched.**
9. **`spec/FEEDBACK.md` updates with the truth**: tick line 18, set `feedback_endpoint` (`:9`) to name the real destination, and **add the WebView2 surface** the current list omits.
10. **Hub-sourced feedback is untrusted content, permanently.** This is a WordMD spec but the rule has to be written where the pipe is opened. `FEEDBACK-LOOP.md:82` and `:133` describe auto-created punchlist items committed via the GitHub API, and `:192` defaults `auto_punchlist` to true. Emily drafts specs from punchlist lines. A public unauthenticated intake feeding that chain is a prompt-injection channel into agent-written specs and commits. Therefore: `auto_punchlist` stays `false` for every app posting to this intake, hub-sourced text is human-triaged before it reaches any punchlist, and any agent reading it wraps it in untrusted-content delimiters exactly as this spec's own "The ask" section does.

## Relationships

- **Blocked by** two reboundman.com items this spec files and does not build: gate `/admin*` (`Caddyfile:83-86`, `server.py:765-769`, `:785-787`), and rate limit `@track` at the Caddy layer (`Caddyfile:75-78`). Both are pre-existing exposures of the live site that WordMD would walk into rather than create.
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
- **Prerequisite gate, checked before this ships**: `/admin` and `/admin/data.json` on reboundman.com return a non-2xx to an unauthenticated request, and `/api/feedback` is rate limited.

## Open questions

**1. Which thing is "the ReboundMan feedback hub": (a) reboundman.com's `/api/feedback` sidecar, promoted to the house intake once its `/admin` inbox is actually gated (Recommended), (b) build `reboundman-feedback-hub` on Firestore as `FEEDBACK-LOOP.md:90` specifies, or (c) neither, WordMD stays local-only?**

Discovery: the house design doc and the one shipped implementation disagree about storage, and neither knows about the other. `FEEDBACK-LOOP.md:181` has the Firestore hub unstarted (no repo, no Firebase project, no auth-family row). The sidecar already stores a per-app `slug` (`server.py:191`) and already documents the endpoint-swap contract (`reboundman.com/spec/FEEDBACK.md:56-60`). **What review changed**: the sidecar is not a working hub today, it is a working *publisher*, because `Caddyfile:83-86` leaves `/admin*` open and `server.py:387-395` renders full message text there. That is a one-line Caddy fix, not a redesign, but it has to happen first, and the first draft's claim that (a) costs "zero server work" was wrong.

Also corrected: (a)'s reversibility was priced as "one endpoint constant." That is the *web widget's* property (`window.RM_FEEDBACK_ENDPOINT` repoints every client on next page load). WordMD is a self-contained WinExe (`WordMD.csproj:16-18`); repointing it means a rebuild, a release, signing, and a user reinstall. Real reversibility for a desktop client is bought at DNS, by pointing the constant at a stable alias.

Priced:
- **(a)** gains a working multi-app inbox for roughly a day of WordMD work plus a Caddy gate and a rate limit. Costs: SQLite on one Railway volume is a single point of loss, and the classifier and digest layers in `FEEDBACK-LOOP.md` sections 3 and 4 still have no home. Reversal is cheap only if the endpoint constant points at an alias, which Behavior 7 requires.
- **(b)** gains the designed end state with Firestore triggers available. Costs a Firebase project, an auth-family row, Cloud Functions, and a repo before WordMD's one-line item can close, and strands the already-live site feedback in a second store meanwhile. Reversal costs a provisioned project you then abandon.
- **(c)** costs nothing and leaves `spec/FEEDBACK.md:18` unchecked forever. Genuinely defensible for a local-first offline editor, and more defensible after review than before it.

Recommendation **(a)**, conditional on the gate. The teaching: the house nearly built its hub by accident, as a site analytics sidecar with a per-app `slug` column. What the accident did not bring along was an access-control model, because an analytics dashboard nobody links to feels private and is not.

**2. On first submission, does WordMD: (a) show a one-time notice naming the destination, with the send proceeding, (b) notice plus a destination-labelled checkbox in the dialog, defaulting to ON (Recommended), or (c) send silently?**

Discovery: `CollectDiagnostics()` (`MainWindow.xaml.cs:1282-1301`) is genuinely clean, and the diagnostics block is already behind a checkbox (`:1262`), so the exposure is the text the user chose to write. But two review findings move the bar. First, the destination publishes (see Model), so the accurate notice is not "we send this to reboundman.com" but "this becomes publicly readable," which is a materially different sentence to consent to. Second, "takes nothing the user did not type" is false at rest: `server.py:185`, `:194-195`, `:201` store an IP-derived `visitor_id` and `ip_hash`, unsalted and joinable against the `hits` table, so the user contributes an identifier they never typed.

The first draft rejected a checkbox on the grounds that an unchecked box on a rare dialog is a no. That reasoning holds for a box defaulting **off**; it does not apply to one defaulting **on**, which preserves the submission rate while making the destination visible at the moment of sending and reversible in one click.

Priced:
- **(a)** gains simplicity, costs the user any per-submission control over a destination that may be public. Reversible.
- **(b)** gains an accurate, revocable, in-context disclosure for one checkbox and one line of copy, and keeps effectively all submissions. Trivially reversible.
- **(c)** spends the trust of a local-first tool to save a sentence.

Recommendation **(b)**, with copy that names the host, says the inbox is private only once the gate lands, and states that an IP-derived identifier is recorded. The teaching: the consent bar is set by what happens to the data, not by how much of it you collect. A clean payload sent to a public page needs more disclosure than a dirty payload sent to a private one.

**3. Sequencing against the two reboundman.com defects: (a) ship WordMD now and file the fixes separately, or (b) block this spec until both land (Recommended)?**

Discovery: the first draft asked a narrower version of this about rate limiting alone and recommended shipping first, on the reasoning that WordMD adds one human-driven caller to an exposure the site already has. Review falsified both halves. The read-path finding means shipping first actively publishes user feedback, which is not a pre-existing exposure WordMD walks past but a new harm WordMD creates by supplying the content. And the rate-limit risk is larger than "the feedback table fills": `server.py:986` is a single `ThreadingHTTPServer` with one global `_db_lock` (`:60`, `:197`) that also answers the Lodge `forward_auth` check (`Caddyfile:106-108`), so saturating `/api/feedback` degrades a paying-attention security gate on a different part of the site. `_LOGIN_SEM` (`:28-30`) shows the author already reasoned about exactly this class of attack, for login only.

Priced:
- **(a)** gains a few days and publishes every submission in the meantime, with no way to unpublish what was already scraped. Irreversible in the only way that matters.
- **(b)** costs one cycle and two small reboundman.com changes, a Caddy `forward_auth` or `basic_auth` block on `/admin*` and a `rate_limit` on `@track`. Both are edits to a file that already contains a working example of each.

Recommendation **(b)**, firmly. The teaching: "this is a pre-existing exposure, not one we create" is a sound argument right up to the moment your change starts feeding the exposure new content. It was the right call for rate limiting in isolation and the wrong one the moment the read path came into view.
