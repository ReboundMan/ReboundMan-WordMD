// Front-matter extract/serialize. Detects YAML --- ... --- block at file head.
export interface FrontMatterSplit {
  frontMatter: string; // raw block including the trailing newline if present
  body: string;
}

const FM_RE = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/;

export function extractFrontMatter(text: string): FrontMatterSplit {
  const m = FM_RE.exec(text);
  if (!m) return { frontMatter: "", body: text };
  return { frontMatter: m[0], body: text.slice(m[0].length) };
}

/** Re-attach front-matter (if any) and convert line endings. */
export function joinFrontMatter(fm: string, body: string, lineEnding: string): string {
  let out = (fm || "") + body;
  if (lineEnding === "\r\n") out = out.replace(/\r\n/g, "\n").replace(/\n/g, "\r\n");
  return out;
}

export function summarizeFrontMatter(fm: string): { fieldCount: number; inner: string } {
  if (!fm) return { fieldCount: 0, inner: "" };
  const inner = fm.replace(/^---\r?\n/, "").replace(/\r?\n---\r?\n?$/, "");
  const lines = inner.split(/\r?\n/).filter((l) => l.trim().length > 0 && !l.trim().startsWith("#"));
  // Crude field count: top-level YAML keys.
  let count = 0;
  for (const l of lines) {
    if (/^\s/.test(l)) continue; // nested
    if (/^[^:]+:/.test(l)) count++;
  }
  return { fieldCount: count, inner };
}
