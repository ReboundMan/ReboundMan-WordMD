---
feature: make-front-matter-editable-in-the-source-pane
status: draft
agent: emily
drafted: 2026-07-29
source: spec/punchlist.md (Next), characteristics (S)(H) confirmed by JJ; targeted /emily run at JJ's request
size: S
priority: H
---

# Make front matter editable in the Source pane (whole-buffer model)

Drafted by Emily on a targeted run, minutes after this gap personally cost JJ: he opened a spec in WordMD to flip its `status:` line and could not see the frontmatter at all. The punchlist item predates that moment; today it earned its (H).

## Problem

WordMD preserves front matter perfectly and lets you do nothing with it. A YAML block at file head is split off at load, shown only as a collapsed "Front-matter: N fields" banner, and re-attached at save. Editing a field (the vault fleet's `status: draft` to `status: ready` flip is now a daily real case) requires leaving WordMD for a text editor, which is exactly backwards for a markdown editor whose own house now runs a frontmatter-driven spec lifecycle.

## Model (what the code has today, cited)

- **Split and join**: `web/src/frontmatter.ts` owns the whole story. `FM_RE` (line 7) matches a `---` fenced YAML head block; `extractFrontMatter` (9-13) splits it from the body; `joinFrontMatter` (16-20) re-attaches at save and restores CRLF; `summarizeFrontMatter` (22-33) counts fields for the banner.
- **Both panes mount body only**: `web/src/doc.ts` splits at load (124-127, into `this.frontMatter` and `this.body`), then mounts Milkdown with `initialMarkdown: this.body` (134-137) and CodeMirror with `initialDoc: this.body` (139-143). The Source pane has never seen the front matter; that is why it cannot edit it.
- **The banner**: read-only `fmBanner`/`fmBody` DOM (82-101), refreshed by `refreshFrontMatterBanner` (285-298), collapsed by default (295). Display only; no edit path.
- **Save path**: `getDocumentText` (200-207): pristine docs pass `originalText` through byte-identical; dirty docs `flush()` the canonical pane into `this.body` and `joinFrontMatter`. This is the "preserves loaded front-matter, regression-free" behavior the punchlist item credits.
- **The coupling that makes this item nontrivial**: the line-anchored scroll sync maps source lines to formatted top-level blocks via `getLineToBlock()` over `this.body` (441-462, cached; block ranges 471-482), consumed by `alignFormattedToSource` (491-552) and `alignSourceToFormatted` (559-639). Every index assumes source text == body. `onPaneEdit` (218-231) likewise assigns pane text straight into `this.body`.
- **No test harness exists**: `web/package.json` scripts are `build`/`watch` only; a search for `*.test.*` across the repo (excluding node_modules) finds nothing. The item's "needs tests" therefore means adding the harness, not extending one.

## Charter

The Source pane mounts the **whole buffer**, front matter included, so YAML edits are first-class and round-trip by construction. The Formatted pane stays body-only (YAML is not markdown to render). The two panes therefore intentionally hold different texts, and the split/join boundary moves from load/save time to pane-sync time.

## Behavior

1. **Load**: unchanged split for state and banner; CodeMirror mounts `frontMatter + body` instead of `body`.
2. **Source edits**: `onPaneEdit("source", text)` re-runs `extractFrontMatter` on the full text, updating `this.frontMatter`, `this.body`, and the banner live. Deleting the closing `---` mid-edit simply makes everything body (regex no-match); nothing is ever dropped because the source pane IS the document. Adding front matter to a bare document works the same way in reverse.
3. **Sync boundaries**: formatted-to-source pushes `joinFrontMatter(fm, body, "\n")`; source-to-formatted pushes the re-split body. `flush()` on a source-canonical doc re-splits rather than assigning raw text to `body`; `getDocumentText` for a source-canonical doc returns the source text directly (it is already whole).
4. **Scroll sync**: all line indices offset by the front-matter line count (derived once per edit alongside the split, cached with the existing `lineToBlockCache` invalidation). Source lines inside the front matter clamp to block 0. Both align functions apply the offset symmetrically.
5. **Banner**: hidden whenever the Source pane is visible (source and split modes), since the real thing is on screen and editable; kept in formatted-only mode as today, where it remains the only front-matter visibility.
6. **Fidelity**: the pristine-doc byte passthrough (200-203) is untouched; a dirty doc saved from source-canonical state must reproduce the loaded bytes when the user made no actual change beyond focus (normalization only at the existing CRLF join point, as today).
7. **Tests (the item's own condition)**: add a minimal vitest (or equivalent) harness to `web/` covering: extract/join round-trip fidelity (CRLF and LF, with and without front matter, malformed fences), the source-edit re-split path including add/remove-fence transitions, and the scroll-sync line-offset math (pure function once extracted). The dual-pane DOM sync itself stays manually verified per the repo's existing practice; say so in the PR.

## Relationships

- **Connects to** `spec/punchlist.md` Ideas "front-matter editable in the Source pane" sibling item about Print (`web/src/print.ts` imports the same split): printing is body-sourced and unaffected by this change, verified by the import shape.
- **Enhances** the vault fleet's spec lifecycle (`C:\Code\Vault\spec\features\emily.md`): WordMD is the `openspec:` target for dashboard spec links, and the status-flip workflow is currently impossible inside it; this feature completes that loop.
- **Contradicts** nothing in `spec/SPEC.md` found; the whole-buffer model is the item's own stated design.

## Acceptance

- Open a spec with front matter, edit `status: draft` to `status: ready` in the Source pane, save: the file on disk shows exactly that one-line change, byte-clean.
- Round-trip fidelity holds across CRLF/LF, fence add/remove mid-session, and split-mode editing; scroll sync in split mode stays aligned on documents with front matter.
- The banner never shows redundantly beside an editable front matter block.
- The new test harness runs in `web/` and covers the three pure surfaces named above.

## Open questions

- Size honesty: JJ confirmed (S). The scroll-sync offset plus the harness bootstrap reads closer to M from the code; flagging, not changing, the confirmed value. If (S) was priced assuming no tests, the harness line above is where to cut.
- Milkdown's formatted pane in formatted-only mode still edits body-only against an invisible-but-collapsed banner; acceptable today, but if front-matter editing should exist there too someday, that is a separate item (form-style field editor), not this one.
