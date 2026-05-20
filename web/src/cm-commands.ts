// CodeMirror source-pane command implementations: wrap selection, prefix lines, insert.
import { EditorView } from "@codemirror/view";

interface SelInfo {
  from: number;
  to: number;
  doc: string;
  selText: string;
}

function getSel(view: EditorView): SelInfo {
  const r = view.state.selection.main;
  const doc = view.state.doc.toString();
  return { from: r.from, to: r.to, doc, selText: doc.slice(r.from, r.to) };
}

function setSel(view: EditorView, anchor: number, head: number): void {
  view.dispatch({ selection: { anchor, head } });
}

function replace(view: EditorView, from: number, to: number, insert: string, selStart?: number, selEnd?: number): void {
  view.dispatch({
    changes: { from, to, insert },
    selection: selStart != null ? { anchor: selStart, head: selEnd ?? selStart } : undefined,
  });
}

function lineRangeAt(view: EditorView, pos: number): { from: number; to: number; text: string } {
  const line = view.state.doc.lineAt(pos);
  return { from: line.from, to: line.to, text: line.text };
}

function selectedLineRange(view: EditorView): { from: number; to: number; text: string } {
  const { from, to } = view.state.selection.main;
  const a = view.state.doc.lineAt(from);
  const b = to > a.to ? view.state.doc.lineAt(to) : a;
  return { from: a.from, to: b.to, text: view.state.doc.sliceString(a.from, b.to) };
}

export function cmWrapInline(view: EditorView, open: string, close: string): void {
  const { from, to, doc, selText } = getSel(view);
  const before = doc.slice(Math.max(0, from - open.length), from);
  const after = doc.slice(to, to + close.length);
  if (before === open && after === close) {
    replace(view, from - open.length, to + close.length, selText, from - open.length, to - open.length);
    return;
  }
  if (selText) {
    replace(view, from, to, open + selText + close, from + open.length, to + open.length);
  } else {
    replace(view, from, to, open + close, from + open.length);
  }
}

export function cmPrefixLines(view: EditorView, prefix: string): void {
  const { from, to, text } = selectedLineRange(view);
  const lines = text.split("\n");
  const allHave = lines.every((l) => l.startsWith(prefix) || l.length === 0);
  const out = allHave
    ? lines.map((l) => (l.startsWith(prefix) ? l.slice(prefix.length) : l)).join("\n")
    : lines.map((l) => prefix + l).join("\n");
  replace(view, from, to, out, from, from + out.length);
}

export function cmPrefixLinesNumbered(view: EditorView): void {
  const { from, to, text } = selectedLineRange(view);
  const lines = text.split("\n");
  const out = lines.map((l, i) => (l.length ? `${i + 1}. ${l}` : l)).join("\n");
  replace(view, from, to, out, from, from + out.length);
}

export function cmToggleHeading(view: EditorView, level: number): void {
  const pos = view.state.selection.main.from;
  const lr = lineRangeAt(view, pos);
  const hashes = "#".repeat(Math.max(1, Math.min(6, level)));
  const stripped = lr.text.replace(/^#{1,6}\s+/, "");
  const newLine = `${hashes} ${stripped}`;
  replace(view, lr.from, lr.to, newLine, lr.from + newLine.length);
}

export function cmInsert(view: EditorView, s: string): void {
  const { from, to } = view.state.selection.main;
  replace(view, from, to, s, from + s.length);
}

export function cmInsertLink(view: EditorView, url: string, label?: string): void {
  const { from, to, selText } = getSel(view);
  const txt = label || selText || "link";
  const out = `[${txt}](${url || ""})`;
  replace(view, from, to, out, from + out.length);
}

export function cmInsertImage(view: EditorView, path: string, alt?: string): void {
  const out = `![${alt || "image"}](${path || ""})`;
  cmInsert(view, out);
}

export function cmInsertCodeBlock(view: EditorView, lang: string): void {
  const { from, to, selText } = getSel(view);
  const fence = "```";
  const insertion = `\n${fence}${lang || ""}\n${selText || ""}\n${fence}\n`;
  const cursor = from + 1 + fence.length + (lang || "").length + 1; // start of body line
  replace(view, from, to, insertion, cursor, cursor + (selText || "").length);
}

export function cmInsertTable(view: EditorView, rows: number, cols: number): void {
  const header = "| " + Array.from({ length: cols }, (_, i) => `Col ${i + 1}`).join(" | ") + " |";
  const sep = "|" + Array.from({ length: cols }, () => "------").join("|") + "|";
  const body = Array.from({ length: rows }, () =>
    "| " + Array.from({ length: cols }, () => " ").join(" | ") + " |"
  ).join("\n");
  cmInsert(view, `\n${header}\n${sep}\n${body}\n`);
}

export function cmClearFormatting(view: EditorView): void {
  const { from, to, selText } = getSel(view);
  if (from === to) return;
  let s = selText;
  s = s
    .replace(/\*\*(.+?)\*\*/g, "$1")
    .replace(/\*(.+?)\*/g, "$1")
    .replace(/~~(.+?)~~/g, "$1")
    .replace(/`([^`]+?)`/g, "$1")
    .replace(/^#{1,6}\s+/gm, "")
    .replace(/^>\s+/gm, "")
    .replace(/^- \[[ xX]\] /gm, "")
    .replace(/^- /gm, "")
    .replace(/^\d+\.\s+/gm, "")
    .replace(/\[(.+?)\]\([^)]*\)/g, "$1")
    .replace(/!\[(.+?)\]\([^)]*\)/g, "$1");
  replace(view, from, to, s, from, from + s.length);
}
