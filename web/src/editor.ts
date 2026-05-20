// WordMD editor entry. Per-tab Doc instances, message dispatch, find/replace.
import { post, on } from "./bridge";
import { Doc } from "./doc";
import {
  cmWrapInline, cmPrefixLines, cmPrefixLinesNumbered, cmToggleHeading,
  cmInsert, cmInsertLink, cmInsertImage, cmInsertCodeBlock, cmInsertTable, cmClearFormatting,
} from "./cm-commands";
import { applyMilkdownCommand, FormatPayload } from "./mk-commands";

const docs = new Map<string, Doc>();
let activeDocId: string | null = null;
let mode: "source" | "formatted" | "split" = "formatted";
let theme: "light" | "dark" = "light";
let zoom = 1.0;
let scrollSync = true;
let lockToSource = true;

const editorHost = document.getElementById("editor-host")!;
const findBar = document.getElementById("find-bar")!;
const findInput = document.getElementById("find-input") as HTMLInputElement;
const replaceInput = document.getElementById("replace-input") as HTMLInputElement;
const findCount = document.getElementById("find-count")!;
const optCase = document.getElementById("opt-case") as HTMLInputElement;
const optWord = document.getElementById("opt-word") as HTMLInputElement;
const optRegex = document.getElementById("opt-regex") as HTMLInputElement;
const replaceOneBtn = document.getElementById("replace-one")!;
const replaceAllBtn = document.getElementById("replace-all")!;

function activeDoc(): Doc | undefined {
  return activeDocId ? docs.get(activeDocId) : undefined;
}

// ---- Bridge handlers ----

on("createDocument", (p: { docId: string; text?: string; lineEnding?: string }) => {
  if (!p?.docId) return;
  if (docs.has(p.docId)) {
    // Replace contents in place (open-into-existing-tab path).
    const existing = docs.get(p.docId)!;
    existing.destroy();
    docs.delete(p.docId);
  }
  const callbacks = {
    onDirty: (docId: string, dirty: boolean) => post("documentDirty", { docId, dirty }),
    onStats: (docId: string, stats: any) => {
      // Only push stats for the active doc to keep the host status bar focused.
      if (docId === activeDocId) post("documentStats", { docId, ...stats });
    },
  };
  const doc = new Doc(p.docId, editorHost, { text: p.text || "", lineEnding: p.lineEnding || "\r\n" }, callbacks);
  doc.setMode(mode);
  doc.setScrollSync(scrollSync);
  doc.setLockToSource(lockToSource);
  doc.setActive(false);
  applyTheme(doc);
  docs.set(p.docId, doc);
});

on("switchDocument", (p: { docId: string }) => {
  if (!p?.docId || !docs.has(p.docId)) return;
  if (activeDocId && docs.has(activeDocId)) docs.get(activeDocId)!.setActive(false);
  activeDocId = p.docId;
  const d = docs.get(activeDocId)!;
  d.setMode(mode);
  d.setActive(true);
});

on("closeDocument", (p: { docId: string }) => {
  if (!p?.docId) return;
  const d = docs.get(p.docId);
  if (!d) return;
  if (activeDocId === p.docId) activeDocId = null;
  d.destroy();
  docs.delete(p.docId);
});

on("loadDocument", (p: { text?: string; lineEnding?: string }) => {
  // Backward-compat shim: load into the active doc.
  if (!activeDocId) return;
  const old = docs.get(activeDocId);
  if (!old) return;
  old.destroy();
  docs.delete(activeDocId);
  const callbacks = {
    onDirty: (docId: string, dirty: boolean) => post("documentDirty", { docId, dirty }),
    onStats: (docId: string, stats: any) => {
      if (docId === activeDocId) post("documentStats", { docId, ...stats });
    },
  };
  const fresh = new Doc(activeDocId, editorHost, { text: p?.text || "", lineEnding: p?.lineEnding || "\r\n" }, callbacks);
  fresh.setMode(mode);
  fresh.setActive(true);
  applyTheme(fresh);
  docs.set(activeDocId, fresh);
});

on("getDocument", (p: { requestId?: string }) => {
  const d = activeDoc();
  const text = d ? d.getDocumentText() : "";
  post("documentText", { requestId: p?.requestId, docId: activeDocId, text });
});

on("applyFormat", (p: FormatPayload) => {
  const d = activeDoc();
  if (!d) return;
  // Decide which pane the command targets.
  const useSource = (mode === "source") || (mode === "split" && d.cm.hasFocus());
  if (useSource) {
    applyToSourcePane(d, p);
  } else {
    applyMilkdownCommand(d.mk, p);
    // Make sure the milkdown view keeps focus after the command (so subsequent typing works).
    requestAnimationFrame(() => d.mk.focus());
  }
});

on("setMode", (p: { mode?: string }) => {
  if (!p?.mode) return;
  if (p.mode !== "source" && p.mode !== "formatted" && p.mode !== "split") return;
  mode = p.mode;
  document.body.dataset.mode = mode;
  for (const d of docs.values()) d.setMode(mode);
});

on("setTheme", (p: { theme?: string }) => {
  theme = p?.theme === "dark" ? "dark" : "light";
  document.body.dataset.theme = theme;
  for (const d of docs.values()) applyTheme(d);
});

on("setZoom", (p: { scale?: number }) => {
  zoom = p?.scale && p.scale > 0 ? p.scale : 1.0;
  document.documentElement.style.fontSize = `${14 * zoom}px`;
});

on("setScrollSync", (p: { enabled?: boolean }) => {
  scrollSync = !!p?.enabled;
  for (const d of docs.values()) d.setScrollSync(scrollSync);
});

on("setLockToSource", (p: { enabled?: boolean }) => {
  lockToSource = !!p?.enabled;
  for (const d of docs.values()) d.setLockToSource(lockToSource);
});

on("openFind", () => openFind(false));
on("openReplace", () => openFind(true));
on("focusEditor", () => activeDoc()?.cm.focus());

// ---- Source-pane (CodeMirror) command implementation ----
function applyToSourcePane(d: Doc, p: FormatPayload): void {
  const view = d.cm.view;
  switch (p.command) {
    case "bold":          cmWrapInline(view, "**", "**"); break;
    case "italic":        cmWrapInline(view, "*", "*"); break;
    case "strikethrough": cmWrapInline(view, "~~", "~~"); break;
    case "inlineCode":    cmWrapInline(view, "`", "`"); break;
    case "heading":       cmToggleHeading(view, p.level || 1); break;
    case "bulletList":    cmPrefixLines(view, "- "); break;
    case "numberedList":  cmPrefixLinesNumbered(view); break;
    case "taskList":      cmPrefixLines(view, "- [ ] "); break;
    case "blockquote":    cmPrefixLines(view, "> "); break;
    case "link":          cmInsertLink(view, p.url || "", p.text); break;
    case "image":         cmInsertImage(view, p.path || "", p.alt); break;
    case "codeBlock":     cmInsertCodeBlock(view, p.lang || ""); break;
    case "table":         cmInsertTable(view, p.rows || 3, p.cols || 3); break;
    case "hr":            cmInsert(view, "\n\n---\n\n"); break;
    case "clearFormat":   cmClearFormatting(view); break;
    default: console.warn("Unknown source-mode command:", p.command);
  }
  d.cm.focus();
}

function applyTheme(d: Doc): void {
  d.host.dataset.theme = theme;
}

// ---- Find / Replace (source-pane only for v1) ----
let findMatches: { from: number; to: number }[] = [];
let findIndex = -1;

function openFind(replaceMode: boolean) {
  findBar.classList.remove("hidden");
  replaceInput.classList.toggle("hidden", !replaceMode);
  replaceOneBtn.classList.toggle("hidden", !replaceMode);
  replaceAllBtn.classList.toggle("hidden", !replaceMode);
  findInput.focus();
  findInput.select();
  // For find to be useful, switch to source mode if we're in formatted-only.
  if (mode === "formatted") {
    mode = "split";
    document.body.dataset.mode = mode;
    for (const d of docs.values()) d.setMode(mode);
    post("modeChanged", { mode });
  }
  runFind();
}

function closeFind() {
  findBar.classList.add("hidden");
  findMatches = [];
  findIndex = -1;
  findCount.textContent = "0 / 0";
  activeDoc()?.cm.focus();
}

function buildPattern(): RegExp | null {
  const q = findInput.value;
  if (!q) return null;
  const flags = "g" + (optCase.checked ? "" : "i");
  if (optRegex.checked) {
    try { return new RegExp(q, flags); } catch { return null; }
  }
  let esc = q.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  if (optWord.checked) esc = `\\b${esc}\\b`;
  return new RegExp(esc, flags);
}

function runFind(): void {
  const d = activeDoc();
  if (!d) return;
  const re = buildPattern();
  findMatches = [];
  if (!re) { findCount.textContent = "0 / 0"; return; }
  const text = d.cm.getText();
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    findMatches.push({ from: m.index, to: m.index + m[0].length });
    if (m.index === re.lastIndex) re.lastIndex++;
  }
  findIndex = findMatches.length ? 0 : -1;
  if (findIndex >= 0) selectMatch(findIndex);
  updateFindCount();
}

function selectMatch(i: number): void {
  if (i < 0 || i >= findMatches.length) return;
  const d = activeDoc();
  if (!d) return;
  const m = findMatches[i];
  d.cm.view.dispatch({
    selection: { anchor: m.from, head: m.to },
    scrollIntoView: true,
  });
  updateFindCount();
}

function updateFindCount(): void {
  findCount.textContent = `${findIndex >= 0 ? findIndex + 1 : 0} / ${findMatches.length}`;
}

function findNext(): void { if (findMatches.length) { findIndex = (findIndex + 1) % findMatches.length; selectMatch(findIndex); } }
function findPrev(): void { if (findMatches.length) { findIndex = (findIndex - 1 + findMatches.length) % findMatches.length; selectMatch(findIndex); } }

function replaceOne(): void {
  if (findIndex < 0) return;
  const d = activeDoc();
  if (!d) return;
  const m = findMatches[findIndex];
  d.cm.view.dispatch({
    changes: { from: m.from, to: m.to, insert: replaceInput.value },
    selection: { anchor: m.from + replaceInput.value.length },
  });
  runFind();
}

function replaceAll(): void {
  const re = buildPattern();
  if (!re) return;
  const d = activeDoc();
  if (!d) return;
  const text = d.cm.getText();
  const next = text.replace(re, replaceInput.value);
  d.cm.view.dispatch({
    changes: { from: 0, to: text.length, insert: next },
  });
  runFind();
}

findInput.addEventListener("input", runFind);
optCase.addEventListener("change", runFind);
optWord.addEventListener("change", runFind);
optRegex.addEventListener("change", runFind);
document.getElementById("find-prev")!.addEventListener("click", findPrev);
document.getElementById("find-next")!.addEventListener("click", findNext);
replaceOneBtn.addEventListener("click", replaceOne);
replaceAllBtn.addEventListener("click", replaceAll);
document.getElementById("find-close")!.addEventListener("click", closeFind);
findInput.addEventListener("keydown", (ev) => {
  if (ev.key === "Enter") { ev.preventDefault(); ev.shiftKey ? findPrev() : findNext(); }
  else if (ev.key === "Escape") { ev.preventDefault(); closeFind(); }
});
replaceInput.addEventListener("keydown", (ev) => {
  if (ev.key === "Escape") closeFind();
});

// ---- Ready ----
post("editorReady", {});
