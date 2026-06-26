// Milkdown host: one Editor per Doc, mounted in the formatted pane.
import { Editor, rootCtx, defaultValueCtx, editorViewCtx, parserCtx, serializerCtx } from "@milkdown/core";
import { commonmark } from "@milkdown/preset-commonmark";
import { gfm } from "@milkdown/preset-gfm";
import { history } from "@milkdown/plugin-history";
import { listener, listenerCtx } from "@milkdown/plugin-listener";
import { Slice } from "@milkdown/prose/model";

export interface MilkdownHostOptions {
  parent: HTMLElement;
  initialMarkdown: string;
  onUserChange: (markdown: string) => void;
}

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
