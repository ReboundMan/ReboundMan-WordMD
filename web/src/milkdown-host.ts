// Milkdown host: one Editor per Doc, mounted in the formatted pane.
import { Editor, rootCtx, defaultValueCtx, editorViewCtx, parserCtx, serializerCtx } from "@milkdown/core";
import { commonmark, codeBlockSchema } from "@milkdown/preset-commonmark";
import { gfm } from "@milkdown/preset-gfm";
import { history } from "@milkdown/plugin-history";
import { listener, listenerCtx } from "@milkdown/plugin-listener";
import { $view } from "@milkdown/utils";
import { Slice } from "@milkdown/prose/model";
import type { NodeViewConstructor } from "@milkdown/prose/view";

export interface MilkdownHostOptions {
  parent: HTMLElement;
  initialMarkdown: string;
  onUserChange: (markdown: string) => void;
}

const COPY_ICON = `<svg width="14" height="14" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true"><rect x="5.5" y="5.5" width="8" height="8" rx="1.5" stroke="currentColor" stroke-width="1.3"/><path d="M3.5 10.5V3.5a1 1 0 0 1 1-1h7" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>`;
const CHECK_ICON = `<svg width="14" height="14" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true"><path d="M3 8.5L6.5 12L13 4.5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

/**
 * Custom view for fenced code blocks, adding a copy-to-clipboard button.
 *
 * This MUST be a ProseMirror NodeView, not a plain DOM mutation (e.g. a
 * MutationObserver appending a <button> into the rendered <pre>) — that was
 * tried first and hung the renderer. ProseMirror runs its own internal
 * MutationObserver over the editor's DOM to detect and reconcile changes it
 * didn't make itself; a foreign node appended inside content it manages reads
 * as an unexpected external edit, and reconciling it while another observer
 * reacts to *that* reconciliation is a synchronous mutation fight that starves
 * the render loop. A NodeView sidesteps this by construction: `contentDOM` is
 * the one element ProseMirror is told to own and reconcile (here, `<code>`,
 * matching the default schema's own `pre > code` shape), and the button lives
 * as a sibling inside `dom` but outside `contentDOM` — never reconciled
 * against the document model, because ProseMirror was never told it owns it.
 */
const codeBlockCopyView: () => NodeViewConstructor = () => () => {
  const pre = document.createElement("pre");
  const code = document.createElement("code");
  pre.appendChild(code);

  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "mk-copy-btn";
  btn.setAttribute("aria-label", "Copy code");
  btn.innerHTML = COPY_ICON;
  btn.addEventListener("click", (e) => {
    e.preventDefault();
    const text = code.textContent ?? "";
    navigator.clipboard.writeText(text).then(
      () => {
        btn.innerHTML = CHECK_ICON;
        btn.classList.add("copied");
        btn.setAttribute("aria-label", "Copied");
        window.setTimeout(() => {
          btn.innerHTML = COPY_ICON;
          btn.classList.remove("copied");
          btn.setAttribute("aria-label", "Copy code");
        }, 1500);
      },
      (err) => console.error("copy code block failed", err)
    );
  });
  pre.appendChild(btn);

  // stopEvent tells ProseMirror to leave events on the button alone rather than
  // interpret a click there as a selection/edit inside the code text. Scoped to
  // just the button (not "true" for everything in `dom`) -- typing, clicking,
  // and selecting inside `contentDOM` itself must still reach ProseMirror
  // normally, or the code block would stop being editable.
  return { dom: pre, contentDOM: code, stopEvent: (event) => btn.contains(event.target as Node) };
};

export class MilkdownHost {
  private editor!: Editor;
  private suppressEcho = false;
  private ready: Promise<void>;

  constructor(private opts: MilkdownHostOptions) {
    this.ready = this.create();
  }

  private async create(): Promise<void> {
    this.editor = await Editor.make()
      .config((ctx) => {
        ctx.set(rootCtx, this.opts.parent);
        ctx.set(defaultValueCtx, this.opts.initialMarkdown);
        ctx.get(listenerCtx).markdownUpdated((_c, md, prev) => {
          if (this.suppressEcho) return;
          if (md === prev) return;
          this.opts.onUserChange(md);
        });
      })
      .use(commonmark)
      .use(gfm)
      .use(history)
      .use(listener)
      .use($view(codeBlockSchema.node, codeBlockCopyView))
      .create();
  }

  whenReady(): Promise<void> {
    return this.ready;
  }

  /** Replace document content programmatically (sync from source pane) without echo. */
  async setMarkdown(md: string): Promise<void> {
    await this.ready;
    if (md === this.getMarkdown()) return;
    this.suppressEcho = true;
    try {
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        const parser = ctx.get(parserCtx);
        const newDoc = parser(md);
        if (!newDoc) return;
        const state = view.state;
        const tr = state.tr.replace(0, state.doc.content.size, new Slice(newDoc.content, 0, 0));
        view.dispatch(tr.setMeta("addToHistory", false));
      });
    } finally {
      // Flip suppressEcho asynchronously so the listener fires once for our setMarkdown
      // and is ignored, but a subsequent user keystroke is treated as user.
      Promise.resolve().then(() => { this.suppressEcho = false; });
    }
  }

  getMarkdown(): string {
    if (!this.editor) return "";
    let out = "";
    try {
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        const serializer = ctx.get(serializerCtx);
        out = serializer(view.state.doc);
      });
    } catch (err) {
      console.error("getMarkdown failed", err);
    }
    return out;
  }

  /**
   * A deep clone of the rendered ProseMirror DOM for the full document, for the
   * print pipeline's "formatted" output. Cloning the live render tree avoids a
   * serialize-to-HTML + reparse round-trip. ProseMirror is not viewport-
   * virtualized, so the clone holds the entire document (unlike CodeMirror,
   * whose DOM only holds the visible viewport).
   */
  getRenderedNodeClone(): HTMLElement | null {
    if (!this.editor) return null;
    let clone: HTMLElement | null = null;
    try {
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        clone = (view.dom as HTMLElement).cloneNode(true) as HTMLElement;
      });
    } catch (err) {
      console.error("getRenderedNodeClone failed", err);
    }
    return clone;
  }

  focus(): void {
    if (!this.editor) {
      // Editor still initializing; focus once it's ready.
      void this.ready.then(() => {
        try {
          this.editor.action((ctx) => {
            const view = ctx.get(editorViewCtx);
            view.focus();
          });
        } catch {}
      });
      return;
    }
    try {
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        view.focus();
      });
    } catch {}
  }

  hasFocus(): boolean {
    if (!this.editor) return false;
    let hf = false;
    try {
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        hf = view.hasFocus();
      });
    } catch {}
    return hf;
  }

  /** Dispatch any ProseMirror command via the editor's view. */
  dispatchCommand(cmd: (state: any, dispatch?: any, view?: any) => boolean): boolean {
    if (!this.editor) return false;
    let result = false;
    try {
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        result = cmd(view.state, view.dispatch, view);
      });
    } catch (err) {
      console.error("dispatchCommand failed", err);
    }
    return result;
  }

  /** Run an arbitrary action against the editor ctx. */
  withCtx<T>(fn: (ctx: any) => T): T | undefined {
    if (!this.editor) return undefined;
    let result: T | undefined;
    try {
      this.editor.action((ctx) => {
        result = fn(ctx);
      });
    } catch (err) {
      console.error("withCtx failed", err);
    }
    return result;
  }

  destroy(): void {
    if (!this.editor) return;
    try {
      this.editor.destroy();
    } catch {}
  }
}
