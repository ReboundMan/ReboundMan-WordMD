# WordMD Punchlist

> Quick capture of features, ideas, and fixes. Newest items go at the top of each section.

## Now (In Progress)

## Next (Up Soon)

- [ ] (M)(H) Decide code-signing path for the Windows installer. Spec drafted 2026-07-29 (features/decide-code-signing-path-for-the-windows-installer.md, status: draft, awaiting review).
- [ ] (S)(H) Keep `VERSION` and `installer\WordMD.iss` aligned before each release. Spec drafted 2026-07-29 (features/keep-version-and-installer-wordmd-iss-aligned-before-each-re.md, status: draft, awaiting review).

## Ideas / Backlog

- [ ] (M)(M) Print: render formatted output from canonical Markdown out-of-band instead of cloning the live ProseMirror DOM, before any viewport-virtualizing or lazy Milkdown plugin lands (otherwise print could silently truncate long docs).
- [ ] (S)(M) Evaluate whether in-app feedback should submit to the ReboundMan feedback hub instead of only saving locally and prefilling GitHub Issues.
- [x] (S?)(L?) Consider a release smoke-test checklist that can be run on a clean Windows VM. removed 2026-07-28 (triage)

## Bugs

## Done

- [x] (S)(H) Make front-matter editable, 2026-07-30. Redesigned by JJ from the whole-buffer model to a banner-pane model: the existing banner expands via caret, and an Edit checkbox (visible only when expanded) swaps the read-only YAML for a textarea. Pane sync and scroll sync untouched; front-matter-only saves leave the body byte-clean. Fleet code-review panel (Hawk, Bolt, Rookie, Lens, Beacon) ran same day; all findings applied (fence-escape guard, persistent removal banner, Escape cancel, accessible toggle, throttled dirty IPC). Spec updated (features/make-front-matter-editable-in-the-source-pane.md). No test harness added; verified by driving the built bundle in a browser.
- [x] Print support: File → Print… (Ctrl+P, follows current view), Print Formatted…, and Print Source….
- [x] Added ProjectPatterns standards docs and repo metadata.
