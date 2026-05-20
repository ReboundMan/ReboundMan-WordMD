// Milkdown command dispatcher. Maps WordMD format commands to ProseMirror/Milkdown
// commands that operate on the active formatted-pane editor.
import type { MilkdownHost } from "./milkdown-host";
import { callCommand } from "@milkdown/utils";
import {
  toggleStrongCommand,
  toggleEmphasisCommand,
  toggleInlineCodeCommand,
  wrapInHeadingCommand,
  wrapInBulletListCommand,
  wrapInOrderedListCommand,
  wrapInBlockquoteCommand,
  insertHrCommand,
  insertImageCommand,
  createCodeBlockCommand,
  turnIntoTextCommand,
} from "@milkdown/preset-commonmark";
import { toggleStrikethroughCommand, insertTableCommand } from "@milkdown/preset-gfm";
import { editorViewCtx } from "@milkdown/core";

export interface FormatPayload {
  command: string;
  level?: number;
  url?: string;
  text?: string;
  path?: string;
  alt?: string;
  lang?: string;
  rows?: number;
  cols?: number;
}

export function applyMilkdownCommand(mk: MilkdownHost, p: FormatPayload): boolean {
  switch (p.command) {
    case "bold":
      mk.withCtx((ctx) => callCommand(toggleStrongCommand.key)(ctx));
      return true;
    case "italic":
      mk.withCtx((ctx) => callCommand(toggleEmphasisCommand.key)(ctx));
      return true;
    case "strikethrough":
      mk.withCtx((ctx) => callCommand(toggleStrikethroughCommand.key)(ctx));
      return true;
    case "inlineCode":
      mk.withCtx((ctx) => callCommand(toggleInlineCodeCommand.key)(ctx));
      return true;
    case "heading":
      mk.withCtx((ctx) => callCommand(wrapInHeadingCommand.key, p.level || 1)(ctx));
      return true;
    case "bulletList":
      mk.withCtx((ctx) => callCommand(wrapInBulletListCommand.key)(ctx));
      return true;
    case "numberedList":
      mk.withCtx((ctx) => callCommand(wrapInOrderedListCommand.key)(ctx));
      return true;
    case "taskList":
      // No first-class command in preset-gfm v7; insert as plain bullet w/ checkbox markdown.
      // Fall back to inserting a "- [ ] " text node via a transaction.
      return false;
    case "blockquote":
      mk.withCtx((ctx) => callCommand(wrapInBlockquoteCommand.key)(ctx));
      return true;
    case "link":
      // Wrap selection as a link via a manual transaction (Milkdown's link is mark-based).
      mk.withCtx((ctx) => insertOrUpdateLink(ctx, p.url || "", p.text));
      return true;
    case "image":
      mk.withCtx((ctx) => callCommand(insertImageCommand.key, { src: p.path || "", alt: p.alt || "" })(ctx));
      return true;
    case "codeBlock":
      mk.withCtx((ctx) => callCommand(createCodeBlockCommand.key, p.lang || "")(ctx));
      return true;
    case "table":
      mk.withCtx((ctx) => callCommand(insertTableCommand.key, { row: p.rows || 3, col: p.cols || 3 })(ctx));
      return true;
    case "hr":
      mk.withCtx((ctx) => callCommand(insertHrCommand.key)(ctx));
      return true;
    case "clearFormat":
      // Best-effort: turn the current block into a plain paragraph.
      mk.withCtx((ctx) => callCommand(turnIntoTextCommand.key)(ctx));
      return true;
    default:
      return false;
  }
}

// Manually toggle/insert a link mark over the current selection.
// Falls back to inserting "[text](url)" raw if no selection.
function insertOrUpdateLink(ctx: any, url: string, label?: string): boolean {
  try {
    const view = ctx.get(editorViewCtx);
    const { state, dispatch } = view;
    const linkType = state.schema.marks.link;
    if (!linkType) return false;
    const { from, to, empty } = state.selection;
    if (empty) {
      const txt = label || "link";
      const node = state.schema.text(txt, [linkType.create({ href: url })]);
      dispatch(state.tr.replaceSelectionWith(node, false));
      return true;
    }
    const tr = state.tr;
    tr.removeMark(from, to, linkType);
    tr.addMark(from, to, linkType.create({ href: url }));
    if (label && label.length) {
      tr.replaceWith(from, to, state.schema.text(label, [linkType.create({ href: url })]));
    }
    dispatch(tr);
    return true;
  } catch {
    return false;
  }
}
