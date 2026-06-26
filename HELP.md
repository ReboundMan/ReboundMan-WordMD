# WordMD User Guide

> The doctor is in. Markdown made painless.

This is the in-app help for **WordMD**. Open it any time from **Help → User Guide**.

---

## View modes

WordMD has three view modes for any open document:

| Mode | What you see | Best for |
| --- | --- | --- |
| **Source** | Raw Markdown only (CodeMirror 6 with syntax highlighting). | Power editing, exact byte-level control. |
| **Formatted** | Rendered WYSIWYG only (Milkdown / ProseMirror). | Reading, light editing, screenshots. |
| **Split** | Both panes side-by-side, both editable. | Writing while watching the rendered output. |

Switch modes from the **View** menu or the toolbar mode buttons. The mode is remembered per file.

---

## Scrolling in Split mode

Split mode has two independent View-menu toggles that control how the panes scroll together:

### 1. Sync Scrolling *(default: ON)*

The master switch.

- **OFF** — the two panes scroll completely independently. Use this when you want to look at one part of the source while editing somewhere else in the formatted view (or vice versa).
- **ON** — scrolling either pane drives the other. *Which algorithm runs* is then controlled by **Lock to Source** (below).

### 2. Lock to Source *(default: ON, requires Sync Scrolling)*

Picks the alignment algorithm used while Sync Scrolling is on. **Both algorithms are bidirectional** — scrolling on either pane scrolls both.

- **Lock to Source OFF — proportional sync.** Whatever fraction you've scrolled in the source pane, the formatted pane goes to the same fraction (and vice versa). Fast and simple, but on long documents the two panes can show different content at the same vertical position because formatted blocks (images, tables, headings) take different amounts of space than their source.
- **Lock to Source ON — line-anchored sync.** The same source content is shown at the top of both panes. WordMD finds the top-level Markdown block at the top of the pane you're scrolling, and aligns the other pane to the matching block. Eliminates drift, but can feel slightly jumpy on documents with very tall formatted blocks (large images, wide tables).

> **Tip.** "Lock to Source" requires "Sync Scrolling." Turning Sync off automatically turns Lock off; turning Lock on automatically turns Sync on. The menu always reflects the active behavior.

### Quick decision guide

| You want… | Sync | Lock |
| --- | --- | --- |
| Panes that scroll completely independently | OFF | (forced OFF) |
| Both panes drift-resistant; same content always at top | ON | ON |
| Both panes proportional; smoothest on long docs even if drift creeps in | ON | OFF |

---

## File handling

- **External changes** — if another app modifies a file you have open, WordMD reloads it automatically. If the other app is mid-write when WordMD tries to read, it retries silently for ~1.5 s before flagging the tab as externally-changed in the status bar. Manual reload (F5 / View → Reload File) still shows an explicit error dialog on failure.
- **Auto-reload toggle** — **View → Auto-reload on External Change** controls this behavior.

---

## Themes

**View → Theme** offers **Light**, **Dark**, and **System**.

- The theme applies to the menu bar, toolbar, status bar, document canvas, **and** the Windows caption (min/max/close) buttons.
- With **System** selected, WordMD follows the OS theme live — flip Windows from Light to Dark and WordMD follows without restart.

---

## Printing

**File → Print** sends the active document to your printer (or to a PDF writer through the standard Windows print dialog).

| Menu item | What prints | Shortcut |
| --- | --- | --- |
| **Print…** | Whatever the current view shows: **Source** mode prints raw Markdown; **Formatted** and **Split** print the rendered output. | Ctrl+P |
| **Print Formatted…** | Always the rendered WYSIWYG output, regardless of the current view. | |
| **Print Source…** | Always the raw Markdown (front-matter included), in a monospace layout. | |

- Printing captures the **whole document**, not just the part currently scrolled into view.
- **Print Source** includes front-matter; **Print Formatted** prints only the rendered body and omits front-matter.
- Editor chrome (menus, tabs, the find bar, the inactive pane) is never printed.

---

## Telemetry & feedback

- **Help → Send Anonymous Usage Data** — opt-in, off by default. Sends feature counts only; never document content or file paths. Stored locally as JSONL too (see **Help → Open Telemetry Log Folder**).
- **Help → Send Feedback…** (Ctrl+Shift+F1) — write a short note; saved locally under **Open Feedback Log Folder**.

---

## Keyboard cheat sheet

| Action | Shortcut |
| --- | --- |
| New tab | Ctrl+T |
| Open file | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Print | Ctrl+P |
| Close tab | Ctrl+W |
| Reload file | F5 |
| Bold / Italic / Strikethrough | Ctrl+B / Ctrl+I / Ctrl+Shift+X |
| Send feedback | Ctrl+Shift+F1 |

---

*See also: [`README.md`](./README.md) for build & install info.*
