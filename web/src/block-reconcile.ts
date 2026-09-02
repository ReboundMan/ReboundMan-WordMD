// Block-level "diff-based save": when the Formatted pane re-serializes a document,
// remark/ProseMirror's serializer normalizes styling even for content the user never
// touched (bullet character, list tightness, escaping) — see spec/features/
// preserve-untouched-blocks-on-formatted-save.md. This reconciles a fresh full-document
// serialization against the ORIGINAL source text, so genuinely-unedited top-level blocks
// keep their exact original bytes, and only blocks that actually changed get the fresh,
// possibly-restyled serializer output.
//
// Correctness rests on two independent safety layers, not on any one heuristic being
// perfect:
//   1. Per-block: a chunk is only ever treated as "reusable original text" if it parses,
//      IN ISOLATION, to a ProseMirror node structurally equal (Node.eq — semantic, not
//      textual) to a node actually present in the live document. Any chunk that fails
//      this (e.g. isolation-parsing differs subtly from whole-document parsing) simply
//      never matches, so that content falls back to fresh serialization — same as today,
//      never wrong, just not improved for that one block.
//   2. Whole-document: the assembled candidate text is re-parsed and compared, with
//      Node.eq, against the actual live document before ever being used. Any assembly
//      bug (wrong offsets, bad separators) fails this check, and the caller falls back
//      to plain full-document serialization — today's behavior, not corruption.
//
// A real limit on layer 2, found in fleet review: it can only ever catch a candidate
// that reparses to the WRONG structure. It cannot catch a candidate that reparses to
// the RIGHT structure using the WRONG original block's bytes — which is possible when
// two (or more) original blocks are structurally equal to each other (Node.eq is
// semantic, so e.g. two thematic breaks written `---` and `***` are indistinguishable
// to it) and the document is reordered so they no longer line up positionally with
// their original neighbors. The LCS alignment below is rank-aware specifically to close
// that gap — see the comment above `computeRanks`.
//
// This module is intentionally pure (no Milkdown/DOM coupling beyond the ProseMirror
// Node/Schema types) so it can be reasoned about and tested in isolation.

import type { Node, Schema } from "@milkdown/prose/model";
import { computeLineToBlock } from "./block-boundaries";

type ParseFn = (text: string) => Node;
type SerializeFn = (node: Node) => string;

interface Chunk {
  /** Exact original text of this chunk, including any trailing blank-line run. */
  text: string;
  /** Character offset into the original body where this chunk begins. */
  startOffset: number;
  /** Character offset where this chunk ends (exclusive). */
  endOffset: number;
  /**
   * The chunk's content parsed on its own, if (and only if) that parse produced
   * exactly one top-level node. Chunks that are blank-only, or that parse to zero
   * or multiple top-level nodes (a link reference definition, a loose list split
   * across chunk boundaries — see splitIntoChunks), get `node: null` and are never
   * treated as "reusable original text" for a specific live node on their own —
   * they only ever appear as part of a verbatim slice alongside a neighboring
   * matched chunk. That is deliberately permissive: a chunk with node === null
   * still rides along verbatim inside a matched run's slice (see the run-building
   * loop below), which is safe as long as the run's ENDPOINTS are genuinely
   * matched — safety layer 2 only ever inspects the parser's view of the result,
   * so bytes the parser produces nothing for (an unmatched null chunk carried
   * along inside a matched run) are invisible to it either way.
   */
  node: Node | null;
}

/**
 * Splits `text` into blank-line-delimited chunks, using the same fence-aware rule
 * doc.ts's getLineToBlock() uses for scroll-sync (a blank line outside a fenced code
 * block starts a new chunk; blank lines inside a fence do not). A blank-line run
 * attaches to the PRECEDING chunk, matching getLineToBlock's own convention.
 *
 * Lossless by construction: chunks partition `text` contiguously with no gaps or
 * overlaps, so concatenating every chunk's `text` in order reproduces `text` exactly.
 * Each chunk is additionally parsed on its own via `parse`, so the caller never needs
 * a second pass.
 */
function splitIntoChunks(text: string, parse: ParseFn): Chunk[] {
  const lines = text.split("\n");
  const blockOf = computeLineToBlock(lines);

  // Group contiguous same-block lines into chunks. A chunk's own text is exactly
  // `lines.slice(i, j).join("\n")` (no trailing separator baked in); the ONE "\n"
  // between two adjacent chunks is attributed to neither chunk's own span, so that
  // slicing [firstChunk.startOffset, lastChunk.endOffset) over a contiguous run of
  // chunks reproduces the original text for that run exactly, inter-chunk
  // separators included.
  const chunks: Chunk[] = [];
  let offset = 0;
  let i = 0;
  while (i < lines.length) {
    const b = blockOf[i];
    let j = i;
    while (j < lines.length && blockOf[j] === b) j++;
    const chunkLines = lines.slice(i, j);
    const chunkText = chunkLines.join("\n");
    const startOffset = offset;
    const endOffset = startOffset + chunkText.length;

    let node: Node | null = null;
    if (chunkLines.some((l) => l.trim().length !== 0)) {
      try {
        const parsed = parse(chunkText);
        let count = 0;
        let only: Node | null = null;
        parsed.forEach((child) => { count++; only = child; });
        if (count === 1) node = only;
      } catch { /* leave node null; this chunk is never reused verbatim-per-node */ }
    }
    chunks.push({ text: chunkText, startOffset, endOffset, node });

    offset = endOffset;
    if (j < lines.length) offset += 1; // the single "\n" separating this chunk from the next
    i = j;
  }
  return chunks;
}

/**
 * For each element, how many EARLIER elements in the same list it is `eq` to — i.e.
 * "this is the k-th occurrence of this exact shape so far." Two elements can only
 * ever be matched against each other (see the composite comparator below) if they
 * share BOTH a Node.eq shape AND this rank, which is the mechanism that prevents the
 * byte-misattribution bug described in the module header: if the original document
 * has two structurally-identical blocks (rank 0 and rank 1), only the live document's
 * own rank-0 occurrence of that shape can match the original's rank-0 occurrence, and
 * likewise for rank 1 — a reorder that puts something else between them can no longer
 * cause the WRONG original's bytes to be attributed to a live position, because doing
 * so would require matching across ranks, which the comparator refuses. Worst case,
 * matching becomes impossible for the block(s) involved and they fall back to fresh
 * serialization — the same safe degradation this module already relies on everywhere
 * else — rather than silently swapping which original bytes land where.
 */
function computeRanks<T>(items: T[], eq: (x: T, y: T) => boolean): number[] {
  const ranks: number[] = new Array(items.length).fill(0);
  for (let i = 0; i < items.length; i++) {
    let rank = 0;
    for (let k = 0; k < i; k++) {
      if (eq(items[k], items[i])) rank++;
    }
    ranks[i] = rank;
  }
  return ranks;
}

/**
 * Standard LCS alignment: which elements of `b` correspond to which element of `a`.
 * Trims the common prefix/suffix before building the DP table (provably safe — a
 * leading/trailing matched pair always belongs to SOME optimal LCS, standard
 * exchange-argument result), which collapses the overwhelmingly common case (one
 * block edited in an otherwise-unchanged document) to a near-empty table instead of
 * the full n*m matrix. The remaining middle is still O(n*m) with `eq` as the inner
 * comparator (here, a recursive ProseMirror Node.eq — not a cheap comparison), so a
 * hard cap gives up on the untrimmed middle rather than risk hanging the UI thread on
 * a pathological document; everything in a given-up-on middle simply falls back to
 * fresh serialization for those blocks, same as any other non-match.
 */
function lcsAlign<T>(
  a: T[],
  b: T[],
  eq: (x: T, y: T) => boolean
): Array<{ matched: boolean; originalIndex: number }> {
  const result: Array<{ matched: boolean; originalIndex: number }> = Array.from({ length: b.length }, () => ({
    matched: false,
    originalIndex: -1,
  }));

  let start = 0;
  while (start < a.length && start < b.length && eq(a[start], b[start])) {
    result[start] = { matched: true, originalIndex: start };
    start++;
  }
  let aEnd = a.length;
  let bEnd = b.length;
  while (aEnd > start && bEnd > start && eq(a[aEnd - 1], b[bEnd - 1])) {
    aEnd--;
    bEnd--;
    result[bEnd] = { matched: true, originalIndex: aEnd };
  }

  const aMid = a.slice(start, aEnd);
  const bMid = b.slice(start, bEnd);
  const n = aMid.length;
  const m = bMid.length;
  if (n === 0 || m === 0) return result;
  if (n * m > 250_000) return result; // give up on the middle; degrades to fresh serialization there

  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = 1; i <= n; i++) {
    for (let j = 1; j <= m; j++) {
      dp[i][j] = eq(aMid[i - 1], bMid[j - 1]) ? dp[i - 1][j - 1] + 1 : Math.max(dp[i - 1][j], dp[i][j - 1]);
    }
  }
  let i = n;
  let j = m;
  while (i > 0 && j > 0) {
    if (eq(aMid[i - 1], bMid[j - 1])) {
      result[start + j - 1] = { matched: true, originalIndex: start + i - 1 };
      i--;
      j--;
    } else if (dp[i - 1][j] >= dp[i][j - 1]) {
      i--;
    } else {
      j--;
    }
  }
  return result;
}

/** Strips trailing blank lines (and the final trailing newline), never reaching into
 * the last non-blank line's own content. A plain `\s+$` strip would also eat trailing
 * whitespace that is semantically part of that last line (an indented code block's
 * final line, or an unclosed fence at EOF) — which then fails safety layer 2 (the
 * candidate no longer reparses equal to the live doc) and silently disables the whole
 * feature for the entire document, on every save, with no diagnostic. */
function stripTrailingBlankLines(s: string): string {
  return s.replace(/(?:\n[ \t]*)+$/, "");
}

/**
 * Reconciles a freshly-edited document against its original source text, preserving
 * exact original bytes for every maximal run of genuinely-unedited top-level blocks.
 * Returns null (never throws) if reconciliation cannot be trusted for this document —
 * callers must fall back to plain full-document serialization in that case.
 */
export function reconcileFormattedSave(
  originalBody: string,
  currentDoc: Node,
  schema: Schema,
  parse: ParseFn,
  serialize: SerializeFn
): string | null {
  try {
    // Defensive only: doc.ts normalizes to "\n" at load and CodeMirror's Text model
    // normalizes on read, so originalBody should already be "\n"-only by the time it
    // reaches here. This module's correctness depends on the line-splitting below
    // agreeing with the parser's own notion of a line, so make that independent of
    // whether every upstream caller actually holds that invariant.
    originalBody = originalBody.replace(/\r\n?/g, "\n");

    // Some node types pick up mount-time-only derived attrs (e.g. a heading's
    // auto-generated anchor `id`) that a bare parse() of the same text does not
    // reproduce, even though nothing about the content actually differs. Comparing
    // chunk-parsed nodes (always bare-parsed) against the live doc's children
    // directly would then treat every such node as "changed" regardless of whether
    // the user touched it. Round-tripping the live doc through the same
    // serialize -> parse pipeline once puts both sides of every comparison through
    // identical processing, so only genuine content differences remain.
    const normalizedCurrentDoc = parse(serialize(currentDoc));
    const currentChildren: Node[] = [];
    currentDoc.forEach((child) => currentChildren.push(child));
    const normalizedChildren: Node[] = [];
    normalizedCurrentDoc.forEach((child) => normalizedChildren.push(child));
    if (currentChildren.length === 0 || normalizedChildren.length !== currentChildren.length) {
      return null; // nothing to reconcile against, or the round-trip itself changed block count
    }

    const chunks = splitIntoChunks(originalBody, parse);
    // Chunks that didn't parse to exactly one top-level node (blank runs, or a
    // multi-node/zero-node parse) can never be a match target themselves; only
    // node-bearing chunks are candidates for the alignment below.
    const contentChunks = chunks.filter((c) => c.node !== null) as Array<Chunk & { node: Node }>;

    const aNodes = contentChunks.map((c) => c.node);
    const aRanks = computeRanks(aNodes, (x, y) => x.eq(y));
    const bRanks = computeRanks(normalizedChildren, (x, y) => x.eq(y));
    const alignment = lcsAlign(
      aNodes.map((node, idx) => ({ node, rank: aRanks[idx] })),
      normalizedChildren.map((node, idx) => ({ node, rank: bRanks[idx] })),
      (x, y) => x.rank === y.rank && x.node.eq(y.node)
    );

    const parts: string[] = [];
    let i = 0;
    while (i < currentChildren.length) {
      const a = alignment[i];
      if (a.matched) {
        // Extend to the maximal run of contiguously-matched blocks. Worked example:
        // six original blocks, edits at indices 1 and 4 only. alignment (by live
        // index) reads [m0, unmatched, m2, m3, unmatched, m5], where mK means
        // "matched to original index K". Starting the outer loop at i=0: a=m0
        // (matched), so the inner while below walks j from 0 to 1 (alignment[1] is
        // unmatched, so it stops at j=0) -- a run of just block 0. i becomes 1, which
        // is unmatched, so that block is freshly serialized alone. i becomes 2: a=m2
        // (matched), inner while walks j from 2 to 3 (alignment[3]=m3, whose
        // originalIndex 3 is exactly alignment[2]'s originalIndex 2 plus one, so it
        // extends; alignment[4] is unmatched, so it stops at j=3) -- a run covering
        // both blocks 2 and 3, sliced verbatim as one contiguous span including
        // whatever blank-line spacing originally separated them. i becomes 4
        // (unmatched, serialized alone), then 5 (matched, a run of just itself).
        let j = i;
        while (
          j + 1 < currentChildren.length &&
          alignment[j + 1].matched &&
          alignment[j + 1].originalIndex === alignment[j].originalIndex + 1
        ) {
          j++;
        }
        const first = contentChunks[a.originalIndex];
        const last = contentChunks[alignment[j].originalIndex];
        parts.push(stripTrailingBlankLines(originalBody.slice(first.startOffset, last.endOffset)));
        i = j + 1;
      } else {
        // Serialize the ACTUAL current node (not the normalized one) so unmatched
        // blocks reflect exactly what the live document contains.
        const wrapper = schema.nodes.doc.create(null, [currentChildren[i]]);
        parts.push(stripTrailingBlankLines(serialize(wrapper)));
        i++;
      }
    }

    const candidate = parts.join("\n\n") + "\n";

    // Safety layer 2: never trust the assembled text without verifying it reparses
    // to a document equal to the live one. Compared against normalizedCurrentDoc,
    // not currentDoc directly, for the same bare-parse-vs-mount reason as above —
    // this is a bare parse too, so it must be compared against the other bare parse.
    // NOTE: this check can only catch a candidate with the WRONG STRUCTURE. It
    // cannot catch a candidate with the right structure built from the wrong
    // original block's bytes (see module header) — that class of bug is prevented
    // upstream, by the rank-aware comparator above, not caught here.
    const reparsed = parse(candidate);
    if (!reparsed.eq(normalizedCurrentDoc)) return null;
    return candidate;
  } catch {
    return null;
  }
}
