---
feature: make-front-matter-editable-in-the-source-pane
status: built
built: 2026-07-30 (implemented directly by JJ, outside Hopper's lane; fleet-reviewed and browser-verified per the sections below)
agent: emily
drafted: 2026-07-29
revised: 2026-07-30
implemented: 2026-07-30
source: spec/punchlist.md (Ideas), characteristics (S)(H) confirmed by JJ; targeted /emily run at JJ's request; redesigned by JJ 2026-07-30
size: S
priority: H
---

# Make front matter editable (front-matter pane model)

Drafted by Emily on a targeted run, minutes after this gap personally cost JJ: he opened a spec in WordMD to flip its `status:` line and could not edit the frontmatter at all. Redesigned and implemented 2026-07-30 at JJ's direction; the original whole-buffer model (mount the Source pane with the full document) is superseded and recorded below only as history.

## Problem

WordMD preserves front matter perfectly and lets you do nothing with it. A YAML block at file head is split off at load, shown only as a collapsed "Front-matter: N fields" banner, and re-attached at save. Editing a field (the vault fleet's `status: draft` to `status: ready` flip is now a daily real case) requires leaving WordMD for a text editor, which is exactly backwards for a markdown editor whose own house now runs a frontmatter-driven spec lifecycle.

## Design decision (JJ, 2026-07-30)

Do **not** mount front matter into the Source pane. Instead, the existing banner becomes its own small expandable pane:

- The grey banner keeps its caret; collapsed shows `▸` and the field count, expanded shows `▾` plus the YAML.
- An **Edit** checkbox lives in the banner header and is only visible when the banner is expanded. Unchecked: the YAML shows read-only, exactly as before. Checked: the read-only view swaps for a textarea holding the inner YAML (fences managed by the app, never typed).
- This keeps both editor panes body-only, so the dual-pane sync, the line-anchored scroll sync, and the pristine-doc byte passthrough are all untouched. The whole-buffer model's hardest part (offsetting every scroll-sync line index by the front-matter line count) simply does not arise.

## Model (as built, cited)

- **Banner DOM**: `web/src/doc.ts` builds a `.fm-header` row: one toggle button carrying both the caret and the summary text (single tab stop, `aria-expanded`, `aria-controls` naming the `.fm-content` wrapper), plus the Edit checkbox label. Below, `.fm-content` holds the read-only `.fm-body` pre and the `.fm-edit` textarea. `setFmCollapsed` is the single point that flips class, glyph, and `aria-expanded` together.
- **Edit path**: the checkbox calls `setFmEditing`; textarea input calls `onFrontMatterEdit`, which re-wraps the inner YAML in `---` fences, marks the doc dirty (throttled), and refreshes the field count live. A line of only `---` would close the fence early on the next load, so such input is held (summary shows the warning) and never committed; exiting edit mode while invalid reverts to the last good value. Escape cancels: it restores the value from edit-mode entry, exits editing, and returns focus to the checkbox.
- **Fidelity guard**: a `paneEdited` flag (set only in `onPaneEdit`, never reset, matching `isDirty`'s session-scoped semantics) gates `flush()` and `syncInactiveFromCanonical()`. A front-matter-only edit sets `isDirty` but not `paneEdited`, so save joins the new fences onto the untouched body instead of re-serializing it through Milkdown.
- **Dirty IPC**: `notifyDirty()` throttles `documentDirty` posts to the host (leading edge immediate, trailing edge 200ms) for both pane and front-matter edits. Posts keep flowing while dirty rather than firing once per transition, because the host clears its own dirty state on save without telling the web layer.
- **Save path**: unchanged. `getDocumentText` still passes pristine docs through byte-identical and joins `frontMatter + body` with CRLF restoration for dirty ones.
- **Styling**: `web/editor.css`; the checkbox and `.fm-content` hide under `.fm-banner.collapsed`, the textarea swaps in under `.editing`.

## Behavior

1. **Collapsed** (default): grey banner, `▸` caret, field count. No checkbox visible. While an edit session is live the summary appends "(editing)" so a collapsed banner never hides that fact.
2. **Expanded**: `▾` caret, read-only YAML, Edit checkbox appears in the header.
3. **Edit on**: textarea replaces the read-only view, prefilled with the inner YAML. Every keystroke updates `frontMatter` and the field count and marks the doc dirty. A bare `---` line shows a warning and is not committed. Escape reverts to the value at edit-mode entry and exits.
4. **Edit off**: read-only view returns, showing the edited YAML. If the user deleted everything, the block is removed from the document but the banner stays visible reading "Front-matter: removed" (while editing it reads "Front-matter: empty (block will be removed)"), so nothing vanishes, focus is not dropped, and re-checking Edit can bring the block back (the textarea's native undo survives).
5. **Save**: a front-matter-only edit produces exactly that change on disk; the body round-trips byte-clean because it never leaves `this.body`.

## Fleet review (2026-07-30)

Full `code-review` panel plus Beacon ran on the initial build (`reviews/front-matter-pane-edit-*.md`, gitignored). All findings applied same day: Hawk's `---` fence-escape guard, the Lens/Beacon removal-trap rework (persistent "removed" banner, no focus drop, honest wording), Escape-cancel, single accessible toggle button with `aria-controls`, collapsed "(editing)" hint, `paneEdited` rename, and Bolt's throttled dirty IPC. No XSS (all DOM writes via `textContent`/`.value`); contrast passes both themes.

## Verified (2026-07-30, browser-driven against the built bundle)

- Load a CRLF doc, expand, edit `status: draft` to `status: ready`, request document text: output byte-identical to input except that one line, CRLF preserved.
- Checkbox hidden while collapsed; visible expanded; caret glyph, `aria-expanded`, and `aria-controls` target all track state through every path including removal.
- Injecting a bare `---` line shows the warning, commits nothing, and (if never fixed) leaves the doc pristine: save returns the original bytes untouched.
- Deleting all YAML then exiting keeps the banner visible as "Front-matter: removed" and saves a body-only document; Escape mid-edit restores the entry snapshot, unchecks Edit, and focuses the checkbox.
- The dual-pane DOM sync stays manually verified per the repo's existing practice.

## Relationships

- **Connects to** `spec/punchlist.md` sibling Print item (`web/src/print.ts` imports the same split): printing is body-sourced and unaffected.
- **Enhances** the vault fleet's spec lifecycle (`C:\Code\Vault\spec\features\emily.md`): the status-flip workflow now works inside WordMD.
- **Supersedes** the whole-buffer model drafted 2026-07-29 (Source pane mounts `frontMatter + body`; scroll-sync line offsets; banner hidden beside an editable source pane). Dropped because the banner-pane model delivers the same daily use case with no changes to pane sync or scroll sync.

## Open questions

- A document with no front matter has no banner, so there is still no way to *add* front matter inside WordMD. Separate, smaller item if the need appears.
- The filename keeps the original `-in-the-source-pane` slug so existing punchlist and dashboard references stay valid; the title above is the real name now.
- The whole-buffer draft's test-harness condition was tied to the scroll-sync changes that no longer exist. `web/` still has no test harness; this implementation was verified by driving the built bundle in a browser (round-trip checks above). Bootstrap the harness when a change next touches the sync math.
