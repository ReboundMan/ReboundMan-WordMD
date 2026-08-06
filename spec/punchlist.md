# WordMD Punchlist

> Quick capture of features, ideas, and fixes. Newest items go at the top of each section.

## Now (In Progress)

## Next (Up Soon)

- [ ] (M)(H) Decide code-signing path for the Windows installer. Spec drafted 2026-07-29 (features/decide-code-signing-path-for-the-windows-installer.md, status: draft, awaiting review).
- [x] (S)(H) Keep `VERSION` and `installer\WordMD.iss` aligned before each release. Implemented 2026-07-30 with v1.6.0: the version is single-sourced from the repo-root `VERSION` file (the csproj reads it at build time, `WordMD.iss` reads it at installer compile time, and the app's status bar and About dialog display the assembly version instead of hardcoded strings), and the new `.github/workflows/release.yml` fails the release build if the tag does not match `VERSION`. Spec drafted 2026-07-29 (features/keep-version-and-installer-wordmd-iss-aligned-before-each-re.md) remains for review against the implementation.

## Ideas / Backlog

- [ ] 2026-08-05 — WordMD bug: the edit button in front matter shrinks the window _(vault: processed/2026-08-05-wordmd-frontmatter-edit-shrinks-window.md)_
- [ ] 2026-08-03 — (S?)(L?) Add a nag screen (or some other deliberate irritation) so that once Stripe integration lands there is something concrete paying removes. Form unspecified (banner, modal, watermark); built ahead of Stripe, which is still in progress. _(vault: processed/2026-08-03-wordmd-nag-screen-stripe-removal.md)_

- [ ] (M)(M) Print: render formatted output from canonical Markdown out-of-band instead of cloning the live ProseMirror DOM, before any viewport-virtualizing or lazy Milkdown plugin lands (otherwise print could silently truncate long docs). Spec drafted 2026-08-05 (features/10065-print-from-canonical-markdown-out-of-band.md, status: draft, awaiting review).
- [ ] (S)(M) Evaluate whether in-app feedback should submit to the ReboundMan feedback hub instead of only saving locally and prefilling GitHub Issues.
- [x] (S?)(L?) Consider a release smoke-test checklist that can be run on a clean Windows VM. removed 2026-07-28 (triage)

## Bugs

- [ ] 2026-07-30 — The icon is correct in the taskbar and on the launch button, but wrong when alt-tabbing between applications. Two different Windows icon surfaces, likely the same icon resource not wired to both. _(vault: processed/2026-07-30-wordmd-taskbar-alttab-icon-bug.md)_

## Done

- [x] (S)(H) Make front-matter editable, 2026-07-30. Redesigned by JJ from the whole-buffer model to a banner-pane model: the existing banner expands via caret, and an Edit checkbox (visible only when expanded) swaps the read-only YAML for a textarea. Pane sync and scroll sync untouched; front-matter-only saves leave the body byte-clean. Fleet code-review panel (Hawk, Bolt, Rookie, Lens, Beacon) ran same day; all findings applied (fence-escape guard, persistent removal banner, Escape cancel, accessible toggle, throttled dirty IPC). Spec updated (features/make-front-matter-editable-in-the-source-pane.md). No test harness added; verified by driving the built bundle in a browser.
- [x] Print support: File → Print… (Ctrl+P, follows current view), Print Formatted…, and Print Source….
- [x] Added ProjectPatterns standards docs and repo metadata.
