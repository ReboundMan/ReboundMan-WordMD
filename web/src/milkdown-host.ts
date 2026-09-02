// Milkdown host: one Editor per Doc, mounted in the formatted pane.
import { Editor, rootCtx, defaultValueCtx, editorViewCtx, parserCtx, serializerCtx, remarkStringifyOptionsCtx } from "@milkdown/core";
import { commonmark, codeBlockSchema, bulletListSchema } from "@milkdown/preset-commonmark";
import { gfm, extendListItemSchemaForTask } from "@milkdown/preset-gfm";
import { history } from "@milkdown/plugin-history";
import { listener, listenerCtx } from "@milkdown/plugin-listener";
import { $view } from "@milkdown/utils";
import { Slice } from "@milkdown/prose/model";
import type { NodeViewConstructor } from "@milkdown/prose/view";
import { reconcileFormattedSave } from "./block-reconcile";

/**
 * Fixes a real upstream bug in @milkdown/preset-commonmark 7.5.8: bullet_list and
 * list_item's parseMarkdown runners stringify the mdast `spread` boolean via a
 * template literal (`${node.spread}`) before storing it as a ProseMirror attr, so
 * a tight (non-spread) list's attrs.spread ends up as the STRING "false" rather
 * than the boolean false. Their own toMarkdown runners then pass that string
 * straight through as the output spread value. Since ANY non-empty string
 * (including "false") is truthy in JS, and something downstream treats spread
 * as a plain truthy check, EVERY list serializes as loose/spread regardless of
 * how it was actually written — confirmed empirically: a manually-constructed
 * node with a real boolean spread:false serializes tight, correctly.
 *
 * ordered_list's own toMarkdown runner already does `node.attrs.spread === "true"`
 * (converts correctly) - only bullet_list and list_item need this fix.
 *
 * This affects every list every time the Formatted pane saves, not just genuinely
 * edited ones: block-reconcile.ts's untouched-block preservation depends on a
 * lossless serialize->parse round-trip to compare "did this actually change", and
 * this bug makes that round-trip lossy for lists specifically.
 *
 * IMPORTANT: this delegates to the PREVIOUS handler's own toMarkdown.runner (via
 * `spec`, i.e. `prev(ctx)`), only pre-correcting the node's `spread` attr first,
 * rather than reimplementing the runner from scratch. That is not a style choice:
 * `list_item`'s schema id is patched a second time by @milkdown/preset-gfm (task
 * lists — checkbox rendering, the `checked` attr, checked-aware markdown I/O), and
 * `$NodeSchema.extendSchema` replaces the entire node registration for that id —
 * last `.use()` wins, whole-schema, not per-field. An earlier version of this fix
 * patched the base `listItemSchema` directly and reimplemented the runner; because
 * this file's `.use()` chain runs after `.use(gfm)`, that reimplementation silently
 * became the FINAL "list_item" registration, deleting GFM's task-list schema
 * outright — checkboxes stopped rendering and `- [x] done` saved back as `- done`.
 * Delegating means this patch rides on top of whichever `list_item` handler it is
 * given (GFM's task-aware one, here) instead of replacing it.
 */
function fixSpreadSerialization<T extends string>(schema: import("@milkdown/utils").$NodeSchema<T>) {
  return schema.extendSchema((prev) => (ctx) => {
    const spec = prev(ctx);
    return {
      ...spec,
      toMarkdown: {
        match: spec.toMarkdown.match,
        runner: (state: any, node: import("@milkdown/prose/model").Node) => {
          const spread = node.attrs.spread === "true" || node.attrs.spread === true;
          const fixed =
            spread === node.attrs.spread ? node : node.type.create({ ...node.attrs, spread }, node.content, node.marks);
          spec.toMarkdown.runner(state, fixed);
        },
      },
    };
  });
}

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
  // The last markdown text pushed programmatically via setMarkdown(), so the
  // listener callback below can recognize its own echo even after suppressEcho
  // has already been cleared. @milkdown/plugin-listener debounces
  // markdownUpdated by 200ms; suppressEcho is cleared on a microtask right after
  // dispatch, which settles long before that 200ms window closes. So by the time
  // the debounced callback for OUR OWN setMarkdown() actually fires, suppressEcho
  // is already false and the callback would wrongly treat a programmatic sync as
  // a real user edit -- flipping lastEditedPane, marking the doc dirty, and (once
  // block-reconcile.ts existed) polluting the pane that's supposed to hold the
  // canonical original text. Comparing the emitted markdown against this latch
  // catches the echo regardless of the debounce's exact timing, and is cleared
  // after one comparison so a genuine subsequent edit is never swallowed.
  private lastPushedMarkdown: string | null = null;
  private ready: Promise<void>;

  constructor(private opts: MilkdownHostOptions) {
    this.ready = this.create();
  }

  private async create(): Promise<void> {
    this.editor = await Editor.make()
      .config((ctx) => {
        ctx.set(rootCtx, this.opts.parent);
        ctx.set(defaultValueCtx, this.opts.initialMarkdown);
        // Dash bullets, not the mdast-util-to-markdown default asterisk. Purely a
        // style choice for GENUINELY edited lists (block-reconcile.ts preserves
        // untouched ones verbatim regardless of this setting) but matches common
        // markdown convention and avoids gratuitously flipping style on save.
        ctx.update(remarkStringifyOptionsCtx, (prev) => ({ ...prev, bullet: "-" as const }));
        ctx.get(listenerCtx).markdownUpdated((_c, md, prev) => {
          if (this.suppressEcho) return;
          if (md === prev) return;
          if (this.lastPushedMarkdown !== null) {
            const isEcho = md === this.lastPushedMarkdown;
            this.lastPushedMarkdown = null;
            if (isEcho) return;
          }
          this.opts.onUserChange(md);
        });
      })
      .use(commonmark)
      .use(gfm)
      .use(history)
      .use(listener)
      .use($view(codeBlockSchema.node, codeBlockCopyView))
      .use(fixSpreadSerialization(bulletListSchema))
      .use(fixSpreadSerialization(extendListItemSchemaForTask))
      .create();
  }

  whenReady(): Promise<void> {
    return this.ready;
  }

  /** Replace document content programmatically (sync from source pane) without echo. */
  async setMarkdown(md: string): Promise<void> {
    await this.ready;
    if (md === this.getMarkdown()) return;
    this.lastPushedMarkdown = md;
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
      // and is ignored, but a subsequent user keystroke is treated as user. The
      // lastPushedMarkdown latch (above) is the real guard against the debounced
      // echo; this flag only short-circuits the common case where the listener
      // happens to fire before the debounce delay would otherwise matter.
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
   * Like getMarkdown(), but preserves the exact original bytes of every top-level
   * block the user did not actually edit, instead of letting the full-document
   * serializer restyle content it never touched (bullet character, list
   * tightness, escaping). See block-reconcile.ts for the two-layer safety design.
   * `originalBody` is whatever this content looked like before this editing
   * session (the loaded/last-synced text) — the baseline reconciliation compares
   * against. Falls back to plain getMarkdown() whenever reconciliation cannot be
   * trusted for this document, which is always at least as good as today's
   * behavior, never worse.
   *
   * Returns `null` only when no value could be produced at all (editor not
   * mounted). Callers must not fold that together with a legitimate empty-string
   * result (the user deleted everything and the document really does serialize
   * to "") — a `if (result)` truthy check on the caller's side would silently
   * discard that deletion by treating "" the same as "nothing happened".
   */
  getMarkdownReconciled(originalBody: string): string | null {
    if (!this.editor) return null;
    try {
      let out = "";
      this.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        const parser = ctx.get(parserCtx);
        const serializer = ctx.get(serializerCtx);
        const reconciled = reconcileFormattedSave(originalBody, view.state.doc, view.state.schema, parser, serializer);
        out = reconciled ?? serializer(view.state.doc);
      });
      return out;
    } catch (err) {
      console.error("getMarkdownReconciled failed", err);
      return this.getMarkdown();
    }
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
