# WordMD Punchlist

> Quick capture of features, ideas, and fixes. Newest items go at the top of each section.

## Now (In Progress)

## Next (Up Soon)

- [ ] Decide code-signing path for the Windows installer.
- [ ] Keep `VERSION` and `installer\WordMD.iss` aligned before each release.

## Ideas / Backlog

- [ ] Make front-matter editable in the Source pane (whole-buffer model): mount the source pane with the full document so front-matter round-trips and edits are not dropped. Deferred from the fleet audit because it changes the dual-pane sync and scroll-sync mapping and needs tests. The current behavior preserves loaded front-matter and is regression-free.
- [ ] Print: render formatted output from canonical Markdown out-of-band instead of cloning the live ProseMirror DOM, before any viewport-virtualizing or lazy Milkdown plugin lands (otherwise print could silently truncate long docs).
- [ ] Evaluate whether in-app feedback should submit to the ReboundMan feedback hub instead of only saving locally and prefilling GitHub Issues.
- [ ] Consider a release smoke-test checklist that can be run on a clean Windows VM.

## Bugs

## Done

- [x] Print support: File → Print… (Ctrl+P, follows current view), Print Formatted…, and Print Source….
- [x] Added ProjectPatterns standards docs and repo metadata.
