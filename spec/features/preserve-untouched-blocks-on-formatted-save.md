---
feature: preserve-untouched-blocks-on-formatted-save
status: built
agent: claude
drafted: 2026-08-16
source: JJ direct ask, 2026-08-16, quoting external analysis of the mangling pattern ("bullets flipped - to *, \_ and <<\< escapes, backslash hard line breaks, non-breaking spaces... the fingerprint of a ProseMirror/remark markdown serializer rewriting the whole file on save"); JJ: "Implement solution 1c so we stop mangling files"
size: M
priority: H
---

# Preserve untouched blocks on Formatted-pane save

Saving a document from the Formatted pane no longer restyles content the user never touched.

## Problem

Every save from the Formatted pane re-serialized the **entire** document through the Milkdown/remark markdown serializer, not just what changed. That serializer makes stylistic choices — bullet character, list tightness, escaping — that do not necessarily match how the file was originally written, so an edit to one paragraph could silently flip every `-` bullet in the file to `*`, or turn a tight list loose by inserting blank lines between items, in lines the user never touched or even scrolled past.

## Root causes found (two, independent)

1. **The serializer's own style defaults.** `mdast-util-to-markdown` defaults to `*` for bullets; nothing in WordMD's Milkdown setup overrode that.
2. **A real upstream bug in `@milkdown/preset-commonmark@7.5.8`.** `bullet_list` and `list_item`'s parser runners stringify the mdast `spread` boolean via a template literal (`` `${node.spread}` ``) before storing it as a ProseMirror attr, so a *tight* list's `attrs.spread` ends up as the **string** `"false"`, not the boolean `false`. Their own serializer runners pass that string straight through. Since any non-empty string (including `"false"`) is truthy in JavaScript, every list serializes as loose regardless of how it was actually written. `ordered_list`'s own runner already guards against this (`node.attrs.spread === "true"`) and was unaffected.

Root cause 2 matters beyond just "lists look different": it makes the serialize→parse round trip **lossy** for lists specifically, which is exactly the property the fix below depends on being lossless.

## Fix, in three parts

### 1. Serializer style options (`milkdown-host.ts`)

`remarkStringifyOptionsCtx` (the officially exposed context slice for `remark-stringify`'s own `Options`) is set to `bullet: "-"`. Only affects blocks that are genuinely re-serialized (either because the diff-based reconciliation below could not preserve them, or because they were actually edited).

### 2. The list-spread bug, patched by delegation

`bulletListSchema` and `@milkdown/preset-gfm`'s `extendListItemSchemaForTask` (**not** the base `listItemSchema` — see the "Critical (Sage)" finding below for why that distinction matters) are each wrapped with `fixSpreadSerialization()`, which overrides only `toMarkdown.runner`. The patch **delegates** to the handler it's given (`spec.toMarkdown.runner(state, fixedNode)`) after coercing `attrs.spread` to a real boolean on a copy of the node, rather than reimplementing the runner. Everything else — schema, parsing, `toDOM`, and (for the list-item case) GFM's own checkbox handling — passes through unchanged.

### 3. Diff-based reconciliation (`block-reconcile.ts`) — the main feature

When the Formatted pane saves, `doc.ts`'s `flush()` calls `MilkdownHost.getMarkdownReconciled(baseline)`, where `baseline` is `doc.ts`'s own `reconcileBaseline` field — see "The reconciliation baseline" below for why that is a distinct field from `this.body`. That function:

1. Splits `baseline` into blank-line-delimited chunks (the same fence-aware rule `doc.ts`'s `getLineToBlock()` uses for scroll-sync — the two now share one implementation, `block-boundaries.ts`), and parses each chunk **in isolation**.
2. Compares those chunk-parsed nodes against the live document's top-level children using ProseMirror's `Node.eq()` — **semantic** equality (node type, text, marks, attrs), not textual — via a rank-aware LCS alignment (see "Duplicate-sibling byte misattribution" below), so insertions, deletions, and reordering are all handled correctly, not just a positional zip.
3. For every **maximal contiguous run** of matched (unchanged) blocks, slices the run's exact original bytes straight out of `baseline` — including whatever blank-line spacing the user originally had between them.
4. For anything unmatched (genuinely new or edited), serializes just that one node fresh.

**Correctness rests on two independent safety layers**, not on any single heuristic being perfect:

- **Per-block**: a chunk only ever counts as "reusable" if its isolated parse is structurally equal to a node actually present in the live document. If isolated parsing ever disagrees with whole-document parsing for some edge case, that chunk simply fails to match — falling back to fresh serialization for that one block, exactly today's behavior, never wrong.
- **Whole-document**: the assembled candidate text is re-parsed and compared, via `Node.eq()`, against the live document before ever being used. Any assembly bug that produces the WRONG STRUCTURE (bad offsets, bad separators) fails this check and falls back to plain full-document serialization. It **cannot** catch an assembly bug that produces the RIGHT structure from the WRONG original block's bytes (two structurally-identical original blocks swapped) — see "Duplicate-sibling byte misattribution" below for how that gap is actually closed, upstream of this check rather than by it.

`reconcileFormattedSave()` never throws; on any error, or whenever it cannot be confident, it returns `null` and the caller (`getMarkdownReconciled`) falls back to the original `getMarkdown()`.

### The heading-id wrinkle

Heading nodes pick up an auto-generated anchor `id` (slugified from their text) at editor **mount** time, which a bare `parser()` call on the same text does not reproduce. Fixed by round-tripping the live document through the same `serialize → parse` pipeline once before comparing (`normalizedCurrentDoc` in `block-reconcile.ts`), so both sides of every comparison go through identical processing.

## Findings from fleet review (Sage, Chaos, Bolt, Rookie) and how each was closed

Given this touches the save path, this went through a full fleet review before shipping. The algorithm itself (chunk-splitting offset math, the maximal-run assembly loop, comparing against `normalizedCurrentDoc`) held up under adversarial review. Four real issues at the seams did not, and are now fixed:

- **Critical (Sage) — the reconciliation baseline was poisoned before it was ever used.** `onPaneEdit()` assigns the raw, un-reconciled full-document serializer output to `this.body` on *every keystroke*, for live consumers like the stats bar. The first implementation used `this.body` as the reconciliation baseline directly — so by the time any save reached `flush()`, the "original" being diffed against was already a once-reserialized copy, not the user's actual prior file content. The bullet and spread fixes independently made full serialization close to byte-identical for simple test documents, which is exactly why this was not caught empirically at first. **Fixed**: `doc.ts` now carries a dedicated `reconcileBaseline` field, set at load and reassigned only at the end of a successful reconcile (formatted pane) or whenever the source pane becomes canonical (`reconcileBaseline = this.body` in `flush()`'s source branch) — never written by `onPaneEdit()`. Verified end-to-end: a document with untouched `+`-bulleted list content, edited twice elsewhere with a real dispatched transaction (not a synthetic bypass), confirmed `this.body` gets its bullets flipped to `-` immediately by `onPaneEdit`'s raw serializer output (the poisoning is real) while the final reconciled save still correctly preserves the original `+` bullets.
- **Critical (Sage) — the list-spread patch deleted GFM's task-list schema outright.** The first implementation patched the base `listItemSchema` and reimplemented `toMarkdown.runner` from scratch. `$NodeSchema.extendSchema` replaces the *entire* node registration for a given id, last `.use()` wins — since this file's chain runs after `.use(gfm)`, that reimplementation silently became the final "list_item" registration, deleting the `checked` attr and GFM's checked-aware markdown I/O. Checkboxes stopped rendering and `- [x] done` saved back as `- done`. **Fixed**: the patch now wraps `extendListItemSchemaForTask` (imported from `@milkdown/preset-gfm`), not the base schema, and delegates to that handler's own `toMarkdown.runner` after coercing `spread` to a boolean, rather than reimplementing it — see "Fix, part 2" above. Verified: a task list (`* [x] done task` / `* [ ] open task`) round-trips byte-identical on a zero-edit save, both before and after an unrelated edit elsewhere in the document.
- **High (Sage) — `suppressEcho` never actually suppressed anything.** `@milkdown/plugin-listener`'s `markdownUpdated` callback is debounced 200ms; `suppressEcho` was cleared on a microtask right after `setMarkdown()`'s dispatch, which settles long before that 200ms window closes. So every programmatic sync (source pane → formatted pane) eventually fired a spurious `onPaneEdit("formatted", ...)` roughly 200ms later, flipping `lastEditedPane`, marking the doc dirty, and feeding a once-more-reserialized copy back into `this.body` — a second route to baseline poisoning, independent of the first. **Fixed**: `setMarkdown()` now latches the exact text it pushed (`lastPushedMarkdown`); the listener callback compares the debounced-in text against that latch (not against the timing-fragile `suppressEcho` flag) and drops it as an echo if it matches, regardless of how long the debounce actually took. Verified: calling `setMarkdown()` directly and waiting 600ms (well past the 200ms debounce) left `paneEdited`, `lastEditedPane`, and `isDirty` all unchanged.
- **Medium (Chaos) — a truthy check on the reconciled markdown could silently discard a legitimate deletion.** `flush()`'s original `if (md) { this.body = md; ... }` treats a deliberately-empty string (user selected all, deleted) the same as "nothing happened," so `this.body` would keep stale prior content and a save would silently revert the user's deletion. **Fixed**: `getMarkdownReconciled()` now returns `string | null` (`null` only when the editor genuinely isn't mounted), and `flush()` checks `md !== null`. Verified: clearing a document's entire content via a real transaction and flushing correctly empties `this.body`, not leaving the old text in place.
- **High (Chaos), acknowledged as a real but narrow gap, closed upstream of the safety check that couldn't catch it — LCS alignment could swap bytes between two original blocks that are structurally identical (`Node.eq()`-equal) but byte-different (e.g. a `---` and a `***` thematic break — both parse to an `hr` node with zero attrs), when the document is reordered so something else sits between them.** Concretely demonstrated (see `web/src/block-reconcile.ts`'s module header and the verification below): original `[Alpha, ---, ***, Zed]`, reordered live to `[hr, Alpha, hr, Zed]` — the alignment could match the live document's *first* hr slot to the *second* original hr's bytes. Whole-document safety layer 2 (`Node.eq()`) cannot catch this by construction: the misattributed bytes are, by definition, semantically indistinguishable from what should be there. **Fixed** by making the LCS comparator rank-aware: each candidate node is paired with "how many earlier same-shape nodes precede it in its own sequence" (`computeRanks()` in `block-reconcile.ts`), and two nodes can only be matched if both their `Node.eq()` shape *and* their rank agree. This cannot eliminate the underlying ambiguity (two truly indistinguishable blocks still have no "correct" answer for which one's bytes belong where), but it makes the specific *cross-attribution* — matching rank 0 of one shape to rank 1 of another occurrence of that shape — structurally impossible; worst case, the ambiguous block(s) fall back to fresh serialization, the same safe degradation used everywhere else in this module. Verified two ways: (1) reran Chaos's own extracted repro against the fixed algorithm; (2) brute-forced all 24 permutations of a 4-element original set with two duplicate elements against a plain unranked LCS, found a concrete divergent case (`H,H,A,B` reordered to `H,A,B,H`), confirmed the unranked version produces a rank-crossing match there and the ranked version does not.

Two further Medium findings (duplicated between Sage and Chaos) were also fixed:

- **The fence-tracking regex toggled on any fence-looking line, regardless of character or run length** — a `~~~` line could wrongly close a `` ``` `` fence (and vice versa), silently collapsing the rest of a document into one chunk (a markdown-about-markdown document, or this repo's own `spec/` tree, being the obvious trigger). This exact bug was independently duplicated in `doc.ts`'s scroll-sync `getLineToBlock()`. **Fixed** by extracting a single shared, corrected implementation (`block-boundaries.ts`'s `computeLineToBlock()`) used by both, which tracks the opening fence's character and run length and only closes on a same-character run at least as long.
- **The trailing-whitespace strip (`.replace(/\s+$/, "")`) could reach into a code block's own final line** (trailing spaces, or an unclosed fence at EOF) and strip content the parser treats as meaningful, failing safety layer 2 and silently disabling the feature for the whole document. **Fixed**: `stripTrailingBlankLines()` now strips only trailing blank *lines* (`/(?:\n[ \t]*)+$/`), never touching the last non-blank line's own content.

A Low finding (Chaos) — no runtime guard inside `block-reconcile.ts` against non-`\n` line endings reaching it, an assumption it silently depended on `doc.ts`/CodeMirror upholding — got a one-line defensive fix: `reconcileFormattedSave()` now normalizes `originalBody` (`\r\n?` → `\n`) itself before splitting into chunks, rather than relying on every caller to have already done so.

A Medium finding (Sage) about the LCS DP's unbounded `O(n·m)` cost with `Node.eq` (a recursive subtree comparison, not a cheap comparator) as the inner loop, uncapped and running on every pane-focus switch: partially addressed by the `formattedDirtySinceFlush` gating below (the pipeline no longer runs at all on a focus switch with nothing new to reconcile), and further hardened directly in `lcsAlign()` — common prefix/suffix are trimmed before the DP table is built (collapsing the common "one block edited" case to a near-empty table), and a hard cap (`n*m > 250_000`) gives up on an untrimmed middle rather than risk hanging the UI thread, degrading to fresh serialization for whatever fell inside the cap.

## The reconciliation baseline, and why flush() is gated

`doc.ts` deliberately keeps three related-but-distinct pieces of state:

- **`this.body`** — updated on every keystroke in either pane (`onPaneEdit`), for live consumers (stats, cross-pane preview). For the formatted pane this is the RAW, un-reconciled serializer output — exactly the thing this feature exists to avoid trusting as "the original."
- **`this.reconcileBaseline`** — the text `block-reconcile.ts` diffs the formatted pane's fresh serialization against. Set at load, and reassigned only (a) at the end of a successful formatted-pane reconcile (to the reconciled result) or (b) whenever the source pane becomes canonical (`flush()`'s source branch sets it to the just-flushed source text). Never written by `onPaneEdit()`.
- **`this.formattedDirtySinceFlush`** — true only while the formatted pane holds a genuine edit that `flush()` has not yet reconciled. `flush()` is called far more often than "the user actually changed something" — `maybeSyncOnFocus()` fires on every pane-focus switch in split mode, and `syncInactiveFromCanonical()` fires on every mode change — so without this flag, the (relatively expensive) reconcile pipeline would rerun on every one of those with nothing new to do. Set `true` only by `onPaneEdit("formatted", ...)`; cleared once `flush()` has actually reconciled that edit.

Verified: after one real edit and a flush (which reconciles), three subsequent no-op `flush()` calls with no new edits in between triggered zero additional calls into `getMarkdownReconciled()`.

## Verified (browser-driven against the built bundle, via the real save pipeline)

All of the following were run through the actual production path — a dispatched ProseMirror transaction (not a synthetic bypass) firing the real `onUserChange` → `onPaneEdit` → `flush()` → `getDocumentText()` chain:

- Zero-edit save: byte-identical output, including a tight bullet list, a separate task list (different bullet character — remark does not merge lists of different markers, confirmed empirically after an initial test document accidentally exercised CommonMark's list-merging rule instead), a code block, and CRLF preservation.
- A real edit to one block, made via two sequential edits with the listener's 200ms debounce elapsing between them (to force `onPaneEdit`'s raw-serializer overwrite to actually happen mid-session): confirms `this.body` gets poisoned along the way and the final reconciled save still correctly preserves untouched `+`-bulleted content regardless.
- Task list checkboxes preserved through an edit elsewhere in the document.
- A cleared (fully emptied) document flushes to a genuinely empty body, not stale prior content.
- Repeated no-op `flush()` calls (simulating focus/mode-switch churn with no new edits) trigger zero additional reconcile-pipeline calls.
- A programmatic `setMarkdown()` sync, waited out past the listener's debounce window, does not mark the document dirty or flip `lastEditedPane`.
- Insert/delete/reorder, whole-document rewrite, empty/single-block documents, ordered lists and blockquotes with no edits: all previously verified and unaffected by this round of fixes.

## Relationships

- Shares its block-boundary rule with `doc.ts`'s `getLineToBlock()` (scroll-sync) via a single extracted implementation, `block-boundaries.ts` — the two used to carry independently-duplicated copies of the same fence-tracking bug; now there is exactly one implementation.
- Front matter is unaffected: it is split off before the body ever reaches Milkdown, and reattached after, per the existing `frontmatter.ts` split/join.

## Open questions / known limits

- **Loose-list chunk-splitting.** A blank-line-delimited chunk is not always one top-level node: a genuine loose list (a blank line between items, all one list in the real document) gets split into multiple chunks by this module's blank-line rule, each of which parses in isolation to a *different* structure than the real multi-item list. This always fails the per-block match safely and falls back to fresh serialization for the whole list — never corrupts — but it silently disables byte-preservation for every loose list in the document. Not fixed: would need list-aware pre-scanning, and always degrades safely.
- **A fence nested inside a list item, indented 4+ spaces from the line start but 0-3 spaces relative to the list item's own content column, is not specially recognized** by `computeLineToBlock()`'s fence tracking (which only understands top-level, non-container-relative indentation). Degrades safely (the affected chunk fails to match and falls back to fresh serialization) but is a known gap; would need a container-aware block parser to close fully.
- Blank-line-run preservation is exact only within a single unbroken run of unchanged blocks; a blank-line-count change immediately adjacent to an edited block falls back to the canonical single-blank-line join. Cosmetic only.
- No dedicated test harness exists for this repo; verification here is browser-driven against the real bundle, per existing practice.
