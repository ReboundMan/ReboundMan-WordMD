// Doc: per-tab state. Owns one Milkdown view (formatted) and one CodeMirror view (source),
// each mounted in their own DOM container under #editor-host. The "active pane is canonical"
// rule keeps sync simple: the most recently edited pane holds the truth, and we sync the
// other pane only at boundary events (pane focus change, mode change, getDocument, save).

import { CodeMirrorHost } from "./codemirror-host";
import { MilkdownHost } from "./milkdown-host";
import { extractFrontMatter, joinFrontMatter, summarizeFrontMatter } from "./frontmatter";
import { editorViewCtx } from "@milkdown/core";

export type Pane = "source" | "formatted";

export interface DocStats {
  chars: number;
  words: number;
  readMinutes: number;
  eol: "CRLF" | "LF";
}

export interface DocCallbacks {
  onDirty: (docId: string, dirty: boolean) => void;
  onStats: (docId: string, stats: DocStats) => void;
}

const WORD_RE = /[A-Za-z0-9_]+(?:'[A-Za-z]+)?/g;

export class Doc {
  readonly docId: string;
  readonly callbacks: DocCallbacks;

  // Per-tab DOM:
  readonly host: HTMLElement;            // outer .tab-host
  readonly sourcePane: HTMLElement;      // .source-pane
  readonly formattedPane: HTMLElement;   // .formatted-pane
  readonly splitter: HTMLElement;        // .splitter
  readonly fmBanner: HTMLElement;        // front-matter summary banner
  readonly fmBody: HTMLElement;          // expanded front-matter content

  cm!: CodeMirrorHost;
  mk!: MilkdownHost;

  // State:
  frontMatter: string = "";
  body: string = "";
  lineEnding: "\r\n" | "\n" = "\r\n";
  isDirty: boolean = false;
  originalText: string = "";   // raw bytes (as string) loaded from disk; for clean-doc passthrough
  lastEditedPane: Pane = "formatted";
  mode: "source" | "formatted" | "split" = "formatted";

  // While syncing one side from the other, suppress the resulting onUserChange callback
  // from re-marking the doc dirty / triggering another sync. Each host already has its own
  // internal echo guard; this is an outer guard for the whole sync operation.
  private syncing: boolean = false;

  // Scroll-sync (split mode). Both ON modes are bidirectional: scrolling
  // either pane drives the other. Only the alignment algorithm differs.
  //  - scrollSync OFF      => panes scroll independently.
  //  - scrollSync ON, lockToSource OFF => proportional two-way (ratio-based; can drift on long docs).
  //  - scrollSync ON, lockToSource ON  => line-anchored two-way (same source content visible in both panes).
  scrollSync: boolean = true;
  lockToSource: boolean = true;
  private scrollSyncing: boolean = false;
  private lineToBlockCache: number[] | null = null;
  // Derived from lineToBlockCache: block index -> contiguous {first,last} source
  // line range. Lets scroll-sync map a block back to its lines in O(1) instead
  // of scanning the whole line->block array each animation frame.
  private blockRangesCache: Array<{ first: number; last: number }> | null = null;
  // Debounce handle for stats emission (word/char counts) so typing doesn't run
  // a full-document scan + cross-process post on every keystroke.
  private statsTimer: number | null = null;

  constructor(docId: string, parent: HTMLElement, initial: { text: string; lineEnding: string }, callbacks: DocCallbacks) {
    this.docId = docId;
    this.callbacks = callbacks;

    // Build per-tab DOM.
    this.host = document.createElement("div");
    this.host.className = "tab-host";
    this.host.dataset.docId = docId;

    this.fmBanner = document.createElement("div");
    this.fmBanner.className = "fm-banner hidden";
    const fmSummary = document.createElement("span");
    fmSummary.className = "fm-summary";
    const fmToggle = document.createElement("button");
    fmToggle.type = "button";
    fmToggle.className = "fm-toggle";
    fmToggle.title = "Show / hide front-matter";
    fmToggle.textContent = "▾";
    fmToggle.setAttribute("aria-label", "Show or hide front-matter");
    fmToggle.setAttribute("aria-expanded", "false");
    this.fmBody = document.createElement("pre");
    this.fmBody.className = "fm-body";
    this.fmBanner.appendChild(fmSummary);
    this.fmBanner.appendChild(fmToggle);
    this.fmBanner.appendChild(this.fmBody);
    fmToggle.addEventListener("click", () => {
      const collapsed = this.fmBanner.classList.toggle("collapsed");
      fmToggle.setAttribute("aria-expanded", String(!collapsed));
    });

    const editorRow = document.createElement("div");
    editorRow.className = "editor-row";

    this.sourcePane = document.createElement("div");
    this.sourcePane.className = "pane source-pane";
    this.splitter = document.createElement("div");
    this.splitter.className = "splitter";
    this.formattedPane = document.createElement("div");
    this.formattedPane.className = "pane formatted-pane";

    editorRow.appendChild(this.sourcePane);
    editorRow.appendChild(this.splitter);
    editorRow.appendChild(this.formattedPane);

    this.host.appendChild(this.fmBanner);
    this.host.appendChild(editorRow);
    parent.appendChild(this.host);

    // Initialize state from incoming text.
    this.lineEnding = (initial.lineEnding === "\n" ? "\n" : "\r\n");
    this.originalText = initial.text || "";
    const normalized = this.originalText.replace(/\r\n/g, "\n");
    const split = extractFrontMatter(normalized);
    this.frontMatter = split.frontMatter;
    this.body = split.body;
    this.refreshFrontMatterBanner();

    // Splitter drag (operates only when in split mode).
    this.installSplitterDrag();

    // Mount the editors.
    this.mk = new MilkdownHost({
      parent: this.formattedPane,
      initialMarkdown: this.body,
      onUserChange: (md) => this.onPaneEdit("formatted", md),
    });
    this.cm = new CodeMirrorHost({
      parent: this.sourcePane,
      initialDoc: this.body,
      onUserChange: (text) => this.onPaneEdit("source", text),
    });

    // Track focus to drive boundary sync.
    const onFocusChange = () => this.maybeSyncOnFocus();
    this.formattedPane.addEventListener("focusin", onFocusChange, true);
    this.sourcePane.addEventListener("focusin", onFocusChange, true);

    // Wire scroll-sync (split mode only; gated by this.scrollSync).
    this.installScrollSync();
  }

  /** Update the per-doc scroll-sync flag (called from the global toggle). */
  setScrollSync(enabled: boolean): void {
    this.scrollSync = enabled;
  }

  /** Update the per-doc lock-to-source mode (called from the global toggle). */
  setLockToSource(enabled: boolean): void {
    this.lockToSource = enabled;
  }

  // ---- Public API used by the dispatcher ----

  setMode(mode: "source" | "formatted" | "split"): void {
    this.mode = mode;
    this.host.dataset.mode = mode;
    // When entering split or switching panes, ensure the inactive pane reflects the canonical body.
    this.syncInactiveFromCanonical();
  }

  setActive(active: boolean): void {
    this.host.style.display = active ? "" : "none";
    if (active) {
      // Re-emit stats so the host status bar reflects this doc.
      this.emitStats();
      // Focus the pane appropriate for the current mode.
      requestAnimationFrame(() => this.focusPreferredPane());
    }
  }

  /** Ensure body holds the canonical text from whichever pane was last edited. */
  flush(): void {
    if (!this.isDirty) return;       // pristine: body already matches originalText
    if (this.lastEditedPane === "formatted") {
      const md = this.mk.getMarkdown();
      if (md) {
        this.body = md;
        this.lineToBlockCache = null;
        this.blockRangesCache = null;
      }
    } else {
      this.body = this.cm.getText();
      this.lineToBlockCache = null;
      this.blockRangesCache = null;
    }
  }

  getDocumentText(): string {
    if (!this.isDirty) {
      // Pristine doc — return original bytes, normalization-free.
      return this.originalText;
    }
    this.flush();
    return joinFrontMatter(this.frontMatter, this.body, this.lineEnding);
  }

  destroy(): void {
    if (this.statsTimer != null) { window.clearTimeout(this.statsTimer); this.statsTimer = null; }
    try { this.mk.destroy(); } catch {}
    try { this.cm.destroy(); } catch {}
    try { this.host.remove(); } catch {}
  }

  // ---- Internal ----

  private onPaneEdit(pane: Pane, newText: string): void {
    if (this.syncing) return;
    this.lastEditedPane = pane;
    this.body = newText;
    this.lineToBlockCache = null;
    this.blockRangesCache = null;
    if (!this.isDirty) {
      this.isDirty = true;
      this.callbacks.onDirty(this.docId, true);
    } else {
      this.callbacks.onDirty(this.docId, true);
    }
    this.scheduleStatsEmit();
  }

  private maybeSyncOnFocus(): void {
    if (this.mode !== "split") return;
    // In split mode, when focus moves into a pane, push the canonical text into it
    // (so it's up-to-date with whatever the user just typed in the OTHER pane).
    // Determine which pane has focus now.
    const focused: Pane = this.mk.hasFocus() ? "formatted" : (this.cm.hasFocus() ? "source" : this.lastEditedPane);
    if (focused === this.lastEditedPane) return;
    // The newly focused pane needs to be updated from the canonical (other pane's) text.
    this.syncing = true;
    try {
      // Pull latest from the formerly-active pane and push into the newly focused pane.
      this.flush();
      if (focused === "formatted") {
        void this.mk.setMarkdown(this.body);
      } else {
        this.cm.setText(this.body);
      }
    } finally {
      this.syncing = false;
    }
    // The newly focused pane is now canonical going forward.
    this.lastEditedPane = focused;
  }

  private syncInactiveFromCanonical(): void {
    // Only sync if we've actually had user edits to propagate. Before any edit,
    // both panes were initialized with the same body text already.
    if (!this.isDirty) return;
    this.syncing = true;
    try {
      this.flush();
      if (this.lastEditedPane === "formatted") {
        this.cm.setText(this.body);
      } else {
        void this.mk.setMarkdown(this.body);
      }
    } finally {
      // Allow microtasks queued by setMarkdown to settle before clearing.
      Promise.resolve().then(() => { this.syncing = false; });
    }
  }

  private focusPreferredPane(): void {
    if (this.mode === "source") this.cm.focus();
    else if (this.mode === "formatted") this.mk.focus();
    else {
      // split — focus whichever was last edited
      if (this.lastEditedPane === "source") this.cm.focus();
      else this.mk.focus();
    }
  }

  private refreshFrontMatterBanner(): void {
    const summary = this.fmBanner.querySelector(".fm-summary") as HTMLElement | null;
    if (!this.frontMatter) {
      this.fmBanner.classList.add("hidden");
      this.fmBody.textContent = "";
      if (summary) summary.textContent = "";
      return;
    }
    const { fieldCount, inner } = summarizeFrontMatter(this.frontMatter);
    this.fmBanner.classList.remove("hidden");
    this.fmBanner.classList.add("collapsed");
    if (summary) summary.textContent = `Front-matter: ${fieldCount} field${fieldCount === 1 ? "" : "s"}`;
    this.fmBody.textContent = inner;
  }

  private emitStats(): void {
    const text = this.body;
    const chars = text.length;
    const words = (text.match(WORD_RE) || []).length;
    const minutes = Math.max(1, Math.round(words / 200));
    this.callbacks.onStats(this.docId, {
      chars,
      words,
      readMinutes: minutes,
      eol: this.lineEnding === "\r\n" ? "CRLF" : "LF",
    });
  }

  // Coalesce per-keystroke stats so a burst of typing runs at most one
  // full-document scan + host post per ~120ms instead of one per key.
  private scheduleStatsEmit(): void {
    if (this.statsTimer != null) window.clearTimeout(this.statsTimer);
    this.statsTimer = window.setTimeout(() => {
      this.statsTimer = null;
      this.emitStats();
    }, 120);
  }

  private installSplitterDrag(): void {
    // Accessibility: expose the splitter as a focusable separator with a
    // keyboard resize affordance (WCAG 2.1.1), not mouse-drag only.
    this.splitter.setAttribute("role", "separator");
    this.splitter.setAttribute("aria-orientation", "vertical");
    this.splitter.setAttribute("aria-label", "Resize panes");
    this.splitter.setAttribute("aria-valuemin", "10");
    this.splitter.setAttribute("aria-valuemax", "90");
    this.splitter.setAttribute("aria-valuenow", "50");
    this.splitter.setAttribute("tabindex", "0");

    let dragging = false;
    this.splitter.addEventListener("mousedown", (e) => {
      if (this.mode !== "split") return;
      dragging = true;
      e.preventDefault();
    });
    window.addEventListener("mousemove", (e) => {
      if (!dragging) return;
      const row = this.splitter.parentElement!;
      const rect = row.getBoundingClientRect();
      const x = Math.max(160, Math.min(rect.width - 160, e.clientX - rect.left));
      this.sourcePane.style.flex = `0 0 ${x}px`;
      this.formattedPane.style.flex = "1 1 0";
      this.splitter.setAttribute("aria-valuenow", String(Math.round((x / rect.width) * 100)));
    });
    window.addEventListener("mouseup", () => { dragging = false; });

    this.splitter.addEventListener("keydown", (e) => {
      if (this.mode !== "split") return;
      let delta = 0;
      if (e.key === "ArrowLeft") delta = -24;
      else if (e.key === "ArrowRight") delta = 24;
      else return;
      e.preventDefault();
      const row = this.splitter.parentElement!;
      const rect = row.getBoundingClientRect();
      const currentPx = this.sourcePane.getBoundingClientRect().width;
      const x = Math.max(160, Math.min(rect.width - 160, currentPx + delta));
      this.sourcePane.style.flex = `0 0 ${x}px`;
      this.formattedPane.style.flex = "1 1 0";
      this.splitter.setAttribute("aria-valuenow", String(Math.round((x / rect.width) * 100)));
    });
  }

  /**
   * Wire scroll-sync. Two algorithms, both bidirectional and both gated on
   * this.scrollSync and split mode:
   *  - lockToSource ON:  line-anchored (source line ↔ formatted top-level block).
   *  - lockToSource OFF: proportional (ratio-based mirror).
   */
  private installScrollSync(): void {
    const findCmScroller = (): HTMLElement | null =>
      this.sourcePane.querySelector(".cm-scroller") as HTMLElement | null;

    let sourceRafPending = false;
    let formattedRafPending = false;

    const onSourceScroll = () => {
      if (this.mode !== "split" || !this.scrollSync || this.scrollSyncing) return;
      if (sourceRafPending) return;
      sourceRafPending = true;
      requestAnimationFrame(() => {
        sourceRafPending = false;
        if (this.lockToSource) this.alignFormattedToSource();
        else this.proportionalSync("source");
      });
    };

    const onFormattedScroll = () => {
      if (this.mode !== "split" || !this.scrollSync || this.scrollSyncing) return;
      if (formattedRafPending) return;
      formattedRafPending = true;
      requestAnimationFrame(() => {
        formattedRafPending = false;
        if (this.lockToSource) this.alignSourceToFormatted();
        else this.proportionalSync("formatted");
      });
    };

    this.formattedPane.addEventListener("scroll", onFormattedScroll, { passive: true });

    // CodeMirror's .cm-scroller is created asynchronously; poll briefly to attach.
    const tryAttachCm = (attempt = 0) => {
      const cmScroller = findCmScroller();
      if (cmScroller) {
        cmScroller.addEventListener("scroll", onSourceScroll, { passive: true });
        return;
      }
      if (attempt < 30) setTimeout(() => tryAttachCm(attempt + 1), 50);
    };
    tryAttachCm();
  }

  /** Proportional ratio-based mirror; either pane can drive. */
  private proportionalSync(origin: "source" | "formatted"): void {
    const cmScroller = this.sourcePane.querySelector(".cm-scroller") as HTMLElement | null;
    if (!cmScroller) return;
    const src = origin === "source" ? cmScroller : this.formattedPane;
    const dst = origin === "source" ? this.formattedPane : cmScroller;
    const denom = Math.max(1, src.scrollHeight - src.clientHeight);
    const ratio = src.scrollTop / denom;
    const targetMax = Math.max(0, dst.scrollHeight - dst.clientHeight);
    this.scrollSyncing = true;
    try {
      dst.scrollTop = ratio * targetMax;
    } finally {
      requestAnimationFrame(() => { this.scrollSyncing = false; });
    }
  }

  /**
   * Compute (or reuse) a per-source-line top-level-block index. Two source
   * lines belong to the same block if there is no blank line between them
   * (excluding blanks inside ``` fenced code). Multi-paragraph constructs
   * like loose lists become separate indices, which is an acceptable
   * approximation for scroll-sync purposes.
   */
  private getLineToBlock(): number[] {
    if (this.lineToBlockCache) return this.lineToBlockCache;
    const text = this.body;
    const lines = text.split("\n");
    const out: number[] = new Array(lines.length).fill(0);
    let block = -1;
    let needBump = true;
    let inFence = false;
    for (let i = 0; i < lines.length; i++) {
      const l = lines[i];
      if (/^\s{0,3}(```|~~~)/.test(l)) inFence = !inFence;
      if (l.trim().length === 0 && !inFence) {
        out[i] = Math.max(0, block);
        needBump = true;
        continue;
      }
      if (needBump) { block++; needBump = false; }
      out[i] = block;
    }
    this.lineToBlockCache = out;
    return out;
  }

  /**
   * Block index -> contiguous source line range, derived once from
   * getLineToBlock() and cached. The line->block map is monotonic
   * non-decreasing (blocks are assigned in document order), so each block owns a
   * single contiguous run of lines. Lets scroll-sync resolve a block's line span
   * in O(1) instead of scanning the array each frame.
   */
  private getBlockRanges(): Array<{ first: number; last: number }> {
    if (this.blockRangesCache) return this.blockRangesCache;
    const map = this.getLineToBlock();
    const ranges: Array<{ first: number; last: number }> = [];
    for (let i = 0; i < map.length; i++) {
      const b = map[i];
      if (!ranges[b]) ranges[b] = { first: i, last: i };
      else ranges[b].last = i;
    }
    this.blockRangesCache = ranges;
    return ranges;
  }

  /**
   * Align the formatted pane to source's top-visible line, with sub-block
   * interpolation for smooth scrolling. The fraction of the source block we
   * are into is mapped to the same fraction of the corresponding formatted
   * block's height, then offset by an additional partial-line factor so each
   * tick of the wheel produces a small, proportional formatted-pane scroll.
   */
  alignFormattedToSource(): void {
    const cmScroller = this.sourcePane.querySelector(".cm-scroller") as HTMLElement | null;
    if (!cmScroller) return;
    const cmView = this.cm.view;
    const rect = cmScroller.getBoundingClientRect();

    // Position at top-left of CM viewport.
    const pos = cmView.posAtCoords({ x: rect.left + 8, y: rect.top + 1 });
    if (pos == null) return;
    const lineObj = cmView.state.doc.lineAt(pos);
    const lineNum = lineObj.number; // 1-based

    // Sub-line fraction: how far into the visible top line the viewport actually is.
    let subLine = 0;
    try {
      const lineCoords = cmView.coordsAtPos(lineObj.from);
      if (lineCoords) {
        const lineHeight = (cmView.coordsAtPos(lineObj.to)?.bottom ?? lineCoords.bottom) - lineCoords.top;
        if (lineHeight > 0) {
          subLine = Math.max(0, Math.min(1, (rect.top - lineCoords.top) / lineHeight));
        }
      }
    } catch { /* ignore */ }

    const lineToBlock = this.getLineToBlock();
    if (lineToBlock.length === 0) return;
    const idx0 = Math.max(0, Math.min(lineToBlock.length - 1, lineNum - 1));
    const blockIdx = lineToBlock[idx0];

    // First/last source line of this block (O(1) via the range cache).
    const ranges = this.getBlockRanges();
    const range = ranges[blockIdx] ?? { first: idx0, last: idx0 };
    const firstLineIdx = range.first;
    const lastLineIdx = range.last;
    const linesInBlock = lastLineIdx - firstLineIdx + 1;
    const lineWithinBlock = (lineNum - 1 - firstLineIdx) + subLine;
    const fraction = Math.max(0, Math.min(1, lineWithinBlock / Math.max(1, linesInBlock)));

    // Find the nth top-level child of the Milkdown editor DOM.
    const viewDom = this.mk.withCtx((ctx: any) => {
      const view = ctx.get(editorViewCtx);
      return view?.dom as HTMLElement | undefined;
    });
    if (!viewDom) return;
    const blocks = Array.from(viewDom.children) as HTMLElement[];
    if (blocks.length === 0) return;
    const target = blocks[Math.max(0, Math.min(blocks.length - 1, blockIdx))];
    if (!target) return;

    // Position of the matching formatted block within the formatted-pane scroll container.
    const containerRect = this.formattedPane.getBoundingClientRect();
    const targetRect = target.getBoundingClientRect();
    const blockTopInScroller = (targetRect.top - containerRect.top) + this.formattedPane.scrollTop;
    const desiredTop = blockTopInScroller + fraction * targetRect.height;

    this.scrollSyncing = true;
    try {
      this.formattedPane.scrollTop = desiredTop;
    } finally {
      requestAnimationFrame(() => { this.scrollSyncing = false; });
    }
  }

  /**
   * Inverse of alignFormattedToSource: find the top-level formatted block at
   * the top of the formatted viewport, compute how far into it we are, then
   * scroll the source pane so the corresponding source line/block is at top.
   */
  alignSourceToFormatted(): void {
    const cmScroller = this.sourcePane.querySelector(".cm-scroller") as HTMLElement | null;
    if (!cmScroller) return;

    const viewDom = this.mk.withCtx((ctx: any) => {
      const view = ctx.get(editorViewCtx);
      return view?.dom as HTMLElement | undefined;
    });
    if (!viewDom) return;
    const blocks = Array.from(viewDom.children) as HTMLElement[];
    if (blocks.length === 0) return;

    const containerRect = this.formattedPane.getBoundingClientRect();
    const viewportTop = containerRect.top;

    // Locate the block straddling viewportTop (or snap to nearest boundary).
    let blockIdx = 0;
    let fraction = 0;
    const lastIdx = blocks.length - 1;
    let found = false;
    for (let i = 0; i <= lastIdx; i++) {
      const r = blocks[i].getBoundingClientRect();
      if (r.bottom < viewportTop) {
        if (i === lastIdx) { blockIdx = lastIdx; fraction = 1; found = true; }
        continue;
      }
      if (r.top >= viewportTop) {
        blockIdx = i;
        fraction = 0;
        found = true;
        break;
      }
      blockIdx = i;
      fraction = Math.max(0, Math.min(1, (viewportTop - r.top) / Math.max(1, r.height)));
      found = true;
      break;
    }
    if (!found) return;

    // Map block index back to its source-line range (O(1) via the range cache).
    const lineToBlock = this.getLineToBlock();
    if (lineToBlock.length === 0) return;
    const ranges = this.getBlockRanges();
    const range = ranges[blockIdx];
    if (!range) return;
    const firstLineIdx = range.first;
    const lastLineIdx = range.last;

    const linesInBlock = lastLineIdx - firstLineIdx + 1;
    const targetWithin = fraction * linesInBlock;
    const targetLineIdx0 = firstLineIdx + Math.floor(targetWithin);
    const subLine = targetWithin - Math.floor(targetWithin);

    const cmView = this.cm.view;
    const totalLines = cmView.state.doc.lines;
    const lineNum = Math.max(1, Math.min(totalLines, targetLineIdx0 + 1));
    const lineObj = cmView.state.doc.line(lineNum);

    let lineCoords;
    try {
      lineCoords = cmView.coordsAtPos(lineObj.from);
    } catch { /* ignore */ }
    if (!lineCoords) return;

    const lineBottom = (() => {
      try { return cmView.coordsAtPos(lineObj.to)?.bottom ?? lineCoords.bottom; }
      catch { return lineCoords.bottom; }
    })();
    const lineHeight = Math.max(1, lineBottom - lineCoords.top);

    const cmRect = cmScroller.getBoundingClientRect();
    const lineTopInScroller = (lineCoords.top - cmRect.top) + cmScroller.scrollTop;
    const desiredTop = lineTopInScroller + subLine * lineHeight;

    this.scrollSyncing = true;
    try {
      cmScroller.scrollTop = Math.max(0, desiredTop);
    } finally {
      requestAnimationFrame(() => { this.scrollSyncing = false; });
    }
  }
}
