// Print pipeline.
//
// Builds a clean, full-document print surface that is independent of the live
// editor chrome (menus, find bar, the inactive pane) and of CodeMirror's
// viewport virtualization. The surface is injected into #print-root, which the
// `@media print` rules in editor.css reveal while hiding #app. WebView2 renders
// the page through Chromium, so window.print() opens the standard print dialog.
//
// Two output modes:
//   - "source":    the raw markdown (front-matter + body) in a monospace <pre>.
//   - "formatted": a clone of the rendered WYSIWYG DOM produced by Milkdown.

export interface PrintSource {
  /** Full raw markdown of the document (front-matter + body). */
  getRawText: () => string;
  /** A clone of the rendered document body (WYSIWYG). May be async. */
  getFormattedNode: () => Promise<HTMLElement | null> | HTMLElement | null;
  /** Document name (no extension) for the print title / default PDF filename. */
  title?: string;
}

function ensurePrintRoot(): HTMLElement {
  let root = document.getElementById("print-root");
  if (!root) {
    root = document.createElement("div");
    root.id = "print-root";
    // Lives outside #app so it survives #app being hidden during printing.
    document.body.appendChild(root);
  }
  return root;
}

// Exactly one cleanup may own #print-root at a time. A second doPrint() call (or
// a stray afterprint from a previous call) must not wipe a newer print surface,
// so cleanup state is module-level, not per-call.
let activeCleanup: (() => void) | null = null;

// The web page title is static, so capture it once. Overlapping prints then
// always restore the same original instead of each other's temporary titles.
const ORIGINAL_TITLE = document.title;

export async function doPrint(mode: "source" | "formatted", src: PrintSource): Promise<void> {
  // If a previous print is still "open", finalize it before starting a new one
  // so the two can't race over #print-root.
  activeCleanup?.();

  const root = ensurePrintRoot();

  if (mode === "source") {
    const pre = document.createElement("pre");
    pre.className = "print-source";
    // textContent escapes the markdown so it prints verbatim.
    pre.textContent = src.getRawText();
    root.replaceChildren(pre);
  } else {
    const node = await src.getFormattedNode();
    const container = document.createElement("div");
    // The "milkdown" class reuses the existing rendered-content styles.
    container.className = "milkdown print-formatted";
    if (node) container.appendChild(node);
    root.replaceChildren(container);
  }

  const prevTitle = ORIGINAL_TITLE;
  if (src.title) document.title = src.title;

  document.body.classList.add("printing");
  document.body.dataset.printMode = mode;

  const mql = window.matchMedia ? window.matchMedia("print") : null;
  let timeoutId: number | null = null;

  const cleanup = () => {
    if (activeCleanup !== cleanup) return; // a newer print already took over
    activeCleanup = null;
    window.removeEventListener("afterprint", cleanup);
    mql?.removeEventListener("change", onMqlChange);
    if (timeoutId != null) window.clearTimeout(timeoutId);
    document.body.classList.remove("printing");
    delete document.body.dataset.printMode;
    document.title = prevTitle;
    root.replaceChildren();
  };
  const onMqlChange = (e: MediaQueryListEvent) => { if (!e.matches) cleanup(); };

  activeCleanup = cleanup;
  // afterprint is the primary signal; matchMedia('print') is a more reliable
  // backstop across WebView2 print paths; the timeout guarantees we never leak
  // the surface if neither fires (e.g. dialog dismissed without an event).
  window.addEventListener("afterprint", cleanup);
  mql?.addEventListener("change", onMqlChange);
  timeoutId = window.setTimeout(cleanup, 120_000);

  // Two animation frames so the injected DOM is fully laid out before the
  // print snapshot is taken.
  requestAnimationFrame(() =>
    requestAnimationFrame(() => {
      try {
        window.print();
      } catch (err) {
        console.error("window.print failed", err);
        cleanup();
      }
    })
  );
}
