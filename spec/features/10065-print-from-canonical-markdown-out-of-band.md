---
spec: 10065
feature: print-from-canonical-markdown-out-of-band
status: draft
agent: emily
drafted: 2026-08-05
revised: 2026-08-05 (Emily: Spec Review Fleet fixes applied — Sage found Behavior contradicted its own Open Question over whether `flush()` should stay, traced `flush()` and confirmed it does write back to `Doc.body` and must stay; also flagged an unhandled cleanup-on-failure gap and a new concurrency window)
source: spec/punchlist.md (Ideas)
size: M
priority: M
---
# Print from canonical Markdown, out-of-band

**ReboundMan-WordMD** · spec **10065** · `print-from-canonical-markdown-out-of-band`

**Spec Review Fleet: Sage SHIP WITH FIXES (4 findings: 1 High, 1 Medium-High, 2 Medium), all fixes applied** (2026-08-05; full review in `reviews-archive/reboundman-wordmd/spec-10065-print-from-canonical-markdown-out-of-band-sage.md`). Headline finding: the first draft's Behavior section said to delete `flush()` while its own Open Questions section recommended keeping it — Sage traced `flush()` (`doc.ts:234-249`) and confirmed it's what makes `Doc.body` canonical at all, not a live-view-sync helper; deleting it would have reintroduced a silent-staleness bug, the exact failure class this spec exists to eliminate.

## The ask

<<<UNTRUSTED PUNCHLIST CONTENT
(M)(M) Print: render formatted output from canonical Markdown out-of-band instead of cloning the live ProseMirror DOM, before any viewport-virtualizing or lazy Milkdown plugin lands (otherwise print could silently truncate long docs).
UNTRUSTED PUNCHLIST CONTENT>>>

Provenance: `spec/punchlist.md` Ideas / Backlog, undated (a legacy item predating the vault's date-on-every-line convention). No vault note or raw capture named; the line is precise enough (specific file behavior, a named future risk) to read as a direct technical note rather than a transcribed capture. `git log -S` for the item's exact phrasing found no filing commit distinct from the punchlist's own history — treated as pre-existing backlog, not separately provenanced.

## Problem

WordMD's "Print Formatted" path clones the live ProseMirror editing view's DOM into a hidden print surface. This works today because ProseMirror renders its entire document into the DOM regardless of scroll position — but the punchlist item names a real, specific future risk: if a viewport-virtualizing rendering strategy or a lazy-loading Milkdown plugin ever lands (neither exists today, confirmed below), the live view's DOM would only contain the visible portion of a long document, and a clone of it would silently print a truncated document with no error. The fix is to stop depending on the live view's DOM at all for print, rendering from the canonical Markdown source out-of-band instead.

## Model (what the code has today, cited)

- **The print pipeline is real and works via DOM cloning, confirmed.** `web/src/editor.ts:158-189` (`on("print", ...)`) is the entry point; for `mode === "formatted"`, it calls `d.mk.getRenderedNodeClone()` (line 186) after conditionally syncing the live editor to canonical text first (lines 182-185: `d.flush()`, then `d.mk.setMarkdown(d.body)` only if `d.lastEditedPane === "source"`, i.e. only when the formatted pane is stale relative to the last source edit). `web/src/milkdown-host.ts:90-102` (`getRenderedNodeClone()`) does `(view.dom as HTMLElement).cloneNode(true)` (line 96) against the live editing view.
- **One nuance the punchlist item's framing slightly overstates**: at print time today, the live view's content is already synced to canonical Markdown when it matters (the `setMarkdown` call on line 184 handles staleness). The bug isn't "prints stale content" — it's "clones whatever DOM the live view currently renders," which is only safe because that DOM currently always holds the full document. The fix target is the same either way: decouple print from the live view's DOM, not fix a staleness bug that doesn't currently exist.
- **The code already names its own risk.** `milkdown-host.ts:83-89`'s doc comment: "ProseMirror is not viewport-virtualized, so the clone holds the entire document (unlike CodeMirror, whose DOM only holds the visible viewport)." This punchlist item is that comment's implied TODO, made explicit.
- **Neither named risk exists yet** (searched repo-wide for `viewport-virtualiz`, `lazy`: no hits outside that comment). This is preemptive hardening against a future change, not a live bug — sizing and urgency should reflect that (see Open question 1).
- **The canonical Markdown source already exists and is exactly what an out-of-band renderer needs.** `web/src/doc.ts`'s `Doc.body: string` field holds the current body Markdown; `getDocumentText()` (lines 251-258) returns the full canonical text. `editor.ts:166,176` already uses this (`raw`) for source-mode printing — formatted-mode printing is the one path still going through the live view instead.
- **`MilkdownHost`'s constructor supports a detached, offscreen instance directly** — no new abstraction needed. `milkdown-host.ts:9-13,20-22`: `MilkdownHostOptions { parent: HTMLElement; initialMarkdown: string; onUserChange: (markdown: string) => void }`, and the constructor immediately creates the editor against whatever `parent` element it's given. `destroy()` (line 166) tears it down. Nothing in the class assumes it's the one-and-only editor instance for a `Doc` — it's a generic wrapper around one Milkdown `Editor`.
- **No existing Markdown→HTML renderer to reuse instead** (searched for `export|toHTML|renderToString|remark|markdown-it` across `web/src` and `src/WordMD`: no hits). Milkdown's own parser, via `setMarkdown`, is the only Markdown-rendering path in this app — a detached `MilkdownHost` instance is the practical way to render out-of-band, not a from-scratch parser.
- **No existing spec touches printing, exporting, or PDF** (searched `spec/features/`): no duplicate-work risk.

## Charter

Formatted-mode print renders from `Doc.getDocumentText()`'s canonical Markdown into a detached, offscreen `MilkdownHost` instance created solely for that print job, never touching or reading from the live editing view's DOM. This makes print correct regardless of whatever rendering strategy the live view uses, present or future.

## Behavior

1. **`editor.ts`'s print handler stops calling `d.mk.getRenderedNodeClone()` for formatted mode.** Instead, `getFormattedNode` (currently lines 177-187) creates a temporary offscreen container **positioned on-screen but visually suppressed** — `position: fixed; top: 0; left: 0; opacity: 0; pointer-events: none;` — **round-1 fix (Sage, Medium-High):** the original draft used `left: -99999px`, which avoids the `display:none` layout-skip pitfall it already knew about but introduces a new one: an element tens of thousands of pixels outside the viewport is exactly the kind of position native `loading="lazy"` and IntersectionObserver-gated rendering treat as "never near enough to render." Milkdown's presets don't lazy-load anything today (confirmed, Model section), but that dependency on viewport-distance heuristics didn't exist when print cloned the live, on-screen editing view — `opacity: 0` at the actual viewport origin avoids it entirely, on-screen in every sense that matters for layout and any future lazy-rendering heuristic, while still invisible to the user. It instantiates a new `MilkdownHost` against that container with `initialMarkdown: d.body` (the same canonical source `getDocumentText()` already exposes), awaits its `ready` promise, calls `getRenderedNodeClone()` **on the new detached instance**, then `destroy()`s it and removes the container — **all four steps (create, await, clone, remove) run inside a `try/finally`, round-1 fix (Sage, Medium): destroy-and-remove happens unconditionally, whether construction, the `ready` await, or cloning succeeds or throws.** If cloning fails, `getFormattedNode` returns `null` (matching `print.ts:60`'s existing null-node handling, so `doPrint` degrades to an empty print-formatted div rather than the handler throwing uncaught out of `on("print", ...)`). The live editing view (`d.mk`, the one actually mounted in the formatted pane) is never read or mutated by this path.
2. **`d.flush()` is kept, unconditionally — only the `lastEditedPane === "source"` / `setMarkdown` branch is dropped.** **Round-1 fix (Sage, High):** the first draft of this spec proposed deleting `flush()` entirely, contradicting its own Open Question 2, which recommended keeping it. Resolved by tracing `flush()` (`doc.ts:234-249`): it is not a live-view-sync helper, it's what makes `Doc.body` canonical at all — for a formatted-pane edit it reads `this.mk.getMarkdown()` (the live editor's current state) and writes it into `this.body` right then; `getDocumentText()` itself unconditionally calls `flush()` before returning canonical text (`doc.ts:256`). The new formatted-print path reads `d.body` directly rather than through `getDocumentText()`, so without an explicit `flush()` call it would print whatever `body` held before the most recent edit was flushed — silently stale, the exact failure class this spec exists to eliminate, reintroduced by the fix meant to prevent it. What genuinely gets dropped is only the `setMarkdown(d.body)` call — that one was solely about pushing canonical text *into* the live view so cloning it would be correct, and has no purpose once print no longer clones the live view.
3. **Source-mode printing (`raw = d.getDocumentText()`, line 166) is unaffected** — it already reads canonical text directly (and already calls `flush()` internally via `getDocumentText()`) and was never part of this problem.
4. **A detached `MilkdownHost` instance is created fresh per print job and destroyed immediately after**, not cached or reused across prints — the print path already isn't latency-sensitive (it's a user-initiated, one-off action gated behind a native print dialog), so the simplicity of "create, render, clone, destroy" outweighs any benefit from pooling an instance.
5. **Concurrent print invocations are explicitly handled, not left as an unstated new race.** **Round-1 fix (Sage, Medium):** before this change, `getFormattedNode()` was synchronous (an immediate `cloneNode`), so two rapid print requests had essentially no window to overlap; `print.ts:45`'s `activeCleanup?.()` guard only tears down a *previous, already-committed* `#print-root`, not a still-in-flight `getFormattedNode()` call. Once `getFormattedNode()` is genuinely async (editor construction plus a `ready` await), a double-click or a menu-click-plus-shortcut in quick succession could build two independent detached instances concurrently. Each is self-contained (no shared state between them), so this is resolved by simply naming it as an accepted, harmless outcome rather than adding new serialization machinery: two concurrent print jobs each get their own correctly-rendered, correctly-cleaned-up detached instance, and whichever `doPrint` call reaches `#print-root` last is what the browser's print dialog shows — the same "last call wins the visible dialog" behavior a synchronous double-print would have had anyway.

## Relationships

- **Connects to** `web/src/doc.ts`'s `Doc.body`/`getDocumentText()`: this spec's whole design rests on that already being the correct canonical source; no changes needed there, just a new consumer.
- **Connects to** `web/src/milkdown-host.ts`'s `MilkdownHost` class: reused as-is via its existing constructor contract, not modified — the class was already generic enough for this use case.
- **No relationship found** to the three other existing specs in this repo (code-signing, VERSION/installer alignment, front-matter editing) — none touch the print or rendering path.

## Acceptance

- Printing "Formatted" produces output rendered from a detached Milkdown instance fed `d.body` directly; the live editing view's DOM is never read by the print path (verifiable by confirming no code path calls `d.mk.getRenderedNodeClone()` for the formatted print case after this ships).
- A long document (long enough that, hypothetically, a virtualized view would only render part of it) still prints in full — not testable against a real virtualization regression today since none exists, but the design no longer depends on the live view's rendering completeness at all, which is the actual acceptance condition.
- Source-mode printing is unaffected — same output as before this change.
- The detached print instance is fully torn down (`destroy()` called, container element removed) after each print job **via `try/finally`, including when construction, the `ready` await, or cloning throws** — no lingering offscreen Milkdown instances accumulate across repeated prints in one session, including failed ones.
- A formatted-pane edit made immediately before printing (before any listener debounce ticks) is reflected in the printed output — `d.flush()` runs unconditionally in the print path, matching `getDocumentText()`'s own behavior.
- The offscreen render container never depends on viewport-distance heuristics (`opacity: 0` at the viewport origin, not an extreme physical offset).

## Open questions

**1. Is this still worth M priority given neither named risk (virtualization, lazy-loading) exists yet?**

Discovery: the punchlist item is explicitly preemptive — "before any viewport-virtualizing or lazy Milkdown plugin lands." Investigation confirmed neither exists in this repo today, and nothing in `spec/features/` or the punchlist proposes adding either currently. This is hardening against a risk with no known trigger date, not a fix for a live bug.

Options:
- **(a) Keep M priority, ship proactively.** Cost: real engineering time now for a risk that may be months or years from mattering. Benefit: once virtualization or lazy-loading does land (from an upstream Milkdown/ProseMirror update, not necessarily a deliberate choice by this repo), this fix is already in place rather than needing to be rushed reactively after a real truncated-print bug report.
- **(b) Downgrade to L priority, revisit if/when virtualization is actually proposed.** Cost: the live-DOM-clone approach keeps working exactly as it does today, indefinitely, until something changes it. Risk: if a Milkdown dependency bump ever silently introduces virtualization (plausible — it's a library update, not necessarily a deliberate architectural decision this repo's own team makes), the truncation bug could ship without anyone connecting it to a dependency bump.

**Emily's recommendation: (a), unchanged from the punchlist's own M.** The fix itself is small and self-contained (Behavior above, no new dependencies, reuses existing code), and the downside of option (b) — a silent, hard-to-diagnose truncation bug arriving via an unrelated dependency bump — is disproportionate to the modest cost of shipping the fix now while it's cheap and well-understood.

**Resolved at round-1 review (was Open question 2): does `Doc.flush()` need to run before reading `d.body` for print?** Yes — traced and settled, no longer open. See Behavior item 2 and the round-1 fix note there: `flush()` (`doc.ts:234-249`) writes the live editor's current markdown into `Doc.body`, making it canonical; `getDocumentText()` depends on the same call. It stays in the print path unconditionally.
