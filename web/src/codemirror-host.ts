// CodeMirror 6 host: a single source-mode editor view per Doc.
import { EditorState, Compartment, Transaction } from "@codemirror/state";
import { EditorView, keymap, highlightActiveLine, lineNumbers, drawSelection } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { markdown } from "@codemirror/lang-markdown";
import { syntaxHighlighting, defaultHighlightStyle, foldGutter, indentOnInput, bracketMatching } from "@codemirror/language";
import { searchKeymap } from "@codemirror/search";

export interface CodeMirrorHostOptions {
  parent: HTMLElement;
  initialDoc: string;
  onUserChange: (newText: string) => void;
}

export class CodeMirrorHost {
  readonly view: EditorView;
  private suppressEcho = false;
  private themeCompartment = new Compartment();

  constructor(opts: CodeMirrorHostOptions) {
    const updateListener = EditorView.updateListener.of((u) => {
      if (!u.docChanged) return;
      if (this.suppressEcho) return;
      // Only treat user-origin transactions as "user changes". Programmatic
      // syncs go through setText() which sets suppressEcho.
      const isUser = u.transactions.some((t) => t.isUserEvent("input") || t.isUserEvent("delete") || t.isUserEvent("undo") || t.isUserEvent("redo") || t.isUserEvent("paste") || t.isUserEvent("move") || t.isUserEvent("select"));
      if (!isUser && !u.transactions.some((t) => !t.annotation(Transaction.userEvent) === false)) {
        // Fall through; CodeMirror often doesn't tag userEvent on plain typing in some setups.
      }
      opts.onUserChange(u.state.doc.toString());
    });

    const state = EditorState.create({
      doc: opts.initialDoc,
      extensions: [
        lineNumbers(),
        history(),
        drawSelection(),
        highlightActiveLine(),
        foldGutter(),
        indentOnInput(),
        bracketMatching(),
        markdown(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        keymap.of([...defaultKeymap, ...historyKeymap, ...searchKeymap, indentWithTab]),
        EditorView.lineWrapping,
        updateListener,
        this.themeCompartment.of([]),
      ],
    });

    this.view = new EditorView({ state, parent: opts.parent });
  }

  /** Replace the document programmatically without firing the user-change callback. */
  setText(text: string): void {
    if (text === this.view.state.doc.toString()) return;
    this.suppressEcho = true;
    try {
      this.view.dispatch({
        changes: { from: 0, to: this.view.state.doc.length, insert: text },
        annotations: Transaction.userEvent.of("sync"),
      });
    } finally {
      this.suppressEcho = false;
    }
  }

  getText(): string {
    return this.view.state.doc.toString();
  }

  focus(): void {
    this.view.focus();
  }

  hasFocus(): boolean {
    return this.view.hasFocus;
  }

  destroy(): void {
    this.view.destroy();
  }
}
