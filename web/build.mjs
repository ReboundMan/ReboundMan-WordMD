// WordMD web bundle build script.
// Bundles src/editor.ts -> dist/editor.js, then copies static assets.
import { build, context } from "esbuild";
import { cp, mkdir, rm, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const srcDir = resolve(here, "src");
const distDir = resolve(here, "dist");
const watch = process.argv.includes("--watch");

async function copyStatics() {
  await mkdir(distDir, { recursive: true });
  await cp(resolve(here, "editor.html"), resolve(distDir, "editor.html"));
  await cp(resolve(here, "editor.css"), resolve(distDir, "editor.css"));
}

async function ensureFresh() {
  if (existsSync(distDir)) await rm(distDir, { recursive: true, force: true });
  await mkdir(distDir, { recursive: true });
}

const buildOptions = {
  entryPoints: [resolve(srcDir, "editor.ts")],
  outfile: resolve(distDir, "editor.js"),
  bundle: true,
  format: "iife",
  target: ["es2020"],
  minify: !watch,
  sourcemap: watch ? "inline" : false,
  legalComments: "none",
  logLevel: "info",
  loader: { ".css": "text" },
};

if (watch) {
  await ensureFresh();
  await copyStatics();
  const ctx = await context(buildOptions);
  await ctx.watch();
  console.log("[wordmd] watching for changes...");
} else {
  await ensureFresh();
  await copyStatics();
  await build(buildOptions);
  // Marker file so MSBuild can detect "dist exists".
  await writeFile(resolve(distDir, ".built"), new Date().toISOString());
  console.log("[wordmd] build complete -> " + distDir);
}
