// WordMD editor entry. Per-tab Doc instances, message dispatch, find/replace.
import { post, on } from "./bridge";
import { Doc } from "./doc";
import {
  cmWrapInline, cmPrefixLines, cmPrefixLinesNumbered, cmToggleHeading,
  cmInsert, cmInsertLink, cmInsertImage, cmInsertCodeBlock, cmInsertTable, cmClearFormatting,
} from "./cm-commands";
import { applyMilkdownCommand, FormatPayload } from "./mk-commands";
import { doPrint } from "./print";

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

on("getDocument", (p: { requestId?: string; docId?: string }) => {
  // Honor an explicit docId from the host so a tab switch between request
  // and response can't cause us to return the wrong document's text. Falls
  // back to the active doc when the host doesn't pin a docId (legacy path).
  const targetId = p?.docId ?? activeDocId;
  const d = targetId ? docs.get(targetId) : undefined;
  if (!d) {
    post("documentText", { requestId: p?.requestId, docId: null, text: null });
    return;
  }
  post("documentText", { requestId: p?.requestId, docId: d.docId, text: d.getDocumentText() });
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

on("print", async (p: { mode?: string; title?: string }) => {
  const d = activeDoc();
  if (!d) return;
  const printMode = p?.mode === "source" ? "source" : "formatted";
  // Empty-document guard. Source print is empty when the whole file is blank;
  // formatted print is empty when the body is blank (front-matter renders to
  // nothing in the formatted view), so a front-matter-only doc still counts as
  // empty for formatted. Tell the host so it can show "Nothing to print".
  const raw = d.getDocumentText();
  const emptyForPrint = printMode === "source"
    ? raw.trim().length === 0
    : d.body.trim().length === 0;
  if (emptyForPrint) {
    post("hostCommand", { command: "printEmpty" });
    return;
  }
  await doPrint(printMode, {
    title: p?.title,
    getRawText: () => raw,
    getFormattedNode: async () => {
      // Formatted print intentionally omits front-matter (source print includes
      // it). Only push the canonical body into Milkdown when the formatted pane
      // is stale (the user edited in source this session); otherwise reading the
      // live render avoids mutating the editor's caret/scroll/selection.
      d.flush();
      if (d.lastEditedPane === "source") {
        await d.mk.setMarkdown(d.body);
      }
      return d.mk.getRenderedNodeClone();
    },
  });
});

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

// ---- Host keyboard shortcuts ----
// WebView2 swallows key input before WinUI's MenuFlyoutItem KeyboardAccelerators
// can fire, so app-level shortcuts (Ctrl+S etc.) never reach the host's File
// menu. We capture them here at the window level and forward to the host as
// "hostCommand" messages. The host routes each command to its existing menu
// handler. Editor-internal shortcuts (Ctrl+B/I, Ctrl+Z/Y, Mod-z undo, etc.)
// are left alone -- we only intercept the file/window-level set, and only when
// the find bar doesn't have focus.
function isInFindBar(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return !!target.closest("#find-bar");
}

window.addEventListener("keydown", (ev) => {
  // Find bar has its own handlers (Enter, Esc, etc.) -- don't claim keys
  // while the user is typing a search query or replacement.
  if (isInFindBar(ev.target)) return;
  if (ev.altKey) return;

  const ctrl = ev.ctrlKey || ev.metaKey;
  const shift = ev.shiftKey;
  const key = ev.key;
  let command: string | null = null;

  if (ctrl && !shift) {
    switch (key.toLowerCase()) {
      case "s": command = "save"; break;
      case "n": command = "new"; break;
      case "o": command = "open"; break;
      case "t": command = "newTab"; break;
      case "w": command = "closeTab"; break;
      case "f": command = "find"; break;
      case "h": command = "replace"; break;
      case "p": command = "print"; break;
    }
  } else if (ctrl && shift) {
    // ev.key for Ctrl+Shift+S is "S" (uppercase) -- compare case-insensitively.
    switch (key.toLowerCase()) {
      case "s": command = "saveAs"; break;
    }
    if (key === "F1") command = "feedback";
  } else if (!ctrl && !shift) {
    if (key === "F5") command = "reload";
    else if (key === "F1") command = "userGuide";
  }

  if (command) {
    ev.preventDefault();
    ev.stopPropagation();
    post("hostCommand", { command });
  }
}, true /* capture, so we beat the editor's own keymaps */);

// ---- Ready ----
post("editorReady", {});
