# WordMD

> **WordMD** ("Word Doctor") -- a friendly, Word-like Markdown editor for Windows.
> *The doctor is in. Markdown made painless.*

[![Release](https://img.shields.io/github/v/release/ReboundMan/ReboundMan-WordMD)](https://github.com/ReboundMan/ReboundMan-WordMD/releases/latest)
[![License](https://img.shields.io/github/license/ReboundMan/ReboundMan-WordMD)](./LICENSE)

## Status

**v1.4.0** -- Tabs, WYSIWYG editing, professional toolbar icons, bidirectional split-mode scroll sync, light/dark/system theming, and silent auto-reload on external file changes. Distributed as an Inno Setup `.exe`; see [`INSTALL.md`](./INSTALL.md).

## Documents

- [`HELP.md`](./HELP.md) -- end-user guide (also opens in-app via **Help → User Guide** / F1)
- [`INSTALL.md`](./INSTALL.md) -- install + build-from-source instructions
- [`LICENSE`](./LICENSE) -- MIT

## Vision

A lightweight, Windows-only desktop app that opens `.md` files natively, gives users a familiar Word-style toolbar UX, and offers a one-click toggle between **Source**, **Formatted**, and **Split** views. Built for writers, PMs, students, and engineers who live in Markdown but want the comfort of a word processor.

## Stack

- **WinUI 3** (.NET 8, C#) -- native Windows shell, menu, toolbar, dialogs
- **WebView2** -- editor host
- **Milkdown** (ProseMirror + Remark) -- Formatted Mode editor (true WYSIWYG)
- **CodeMirror 6** -- Source Mode editor with markdown syntax highlighting
- **esbuild** -- web bundle, invoked automatically by MSBuild
- **Markdig** -- CommonMark + GFM parsing on the .NET side
- **Inno Setup** -- distributable installer
- Local JSONL telemetry hook (opt-in, off by default)

## License

[MIT](./LICENSE) © ReboundMan.

