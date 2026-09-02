// Shared "which top-level block does this line belong to" rule, used by both
// block-reconcile.ts (save-path reconciliation) and doc.ts (scroll-sync). A
// blank line outside a fenced code block starts a new block; a blank line
// attaches to the PRECEDING block. Single source of truth: this rule used to
// be duplicated in both places with a fence-tracking bug (any fence-looking
// line toggled fence state regardless of character or run length, so a
// shorter/different-character fence line nested inside a longer fence closed
// it early) present in both copies identically -- fixed once, here.
//
// Known limitation, accepted rather than solved: a fence's own indentation is
// only recognized up to 3 spaces from the start of the line, per CommonMark's
// rule for a top-level fence. A fence nested inside a list item can be
// visually indented 4+ spaces from the line start while still being 0-3
// spaces relative to the list item's own content column; that case is not
// specially handled here (would require a container-aware block parser).
// Degrades safely wherever it matters: block-reconcile.ts's per-block match
// check simply fails for a misjudged chunk and falls back to fresh
// serialization, and doc.ts's scroll-sync is a UX nicety already tolerant of
// approximate block boundaries.
export function computeLineToBlock(lines: string[]): number[] {
  const out: number[] = new Array(lines.length).fill(0);
  let block = -1;
  let needBump = true;
  let fenceChar: string | null = null;
  let fenceLen = 0;
  for (let i = 0; i < lines.length; i++) {
    const l = lines[i];
    const m = /^\s{0,3}(`{3,}|~{3,})/.exec(l);
    if (m) {
      const marker = m[1];
      const ch = marker[0];
      const len = marker.length;
      if (fenceChar === null) {
        fenceChar = ch;
        fenceLen = len;
      } else if (ch === fenceChar && len >= fenceLen) {
        // Only a same-character run at least as long as the opener closes it,
        // per CommonMark -- a `~~~` line never closes a ``` ``` ``` fence and
        // vice versa, and a longer opener needs an equally long closer.
        fenceChar = null;
        fenceLen = 0;
      }
      // Otherwise: a fence-looking line of the wrong character or run length
      // while already inside a fence is CONTENT (an example fence nested
      // inside a longer outer fence), not a boundary -- fence state is
      // unchanged.
    }
    const inFence = fenceChar !== null;
    if (l.trim().length === 0 && !inFence) {
      out[i] = Math.max(0, block);
      needBump = true;
      continue;
    }
    if (needBump) { block++; needBump = false; }
    out[i] = block;
  }
  return out;
}
