#!/usr/bin/env -S deno run -A
// Re-copies the vendored ASP.NET Core sources from a local dotnet/aspnetcore checkout, prepending
// the provenance header every vendored file carries. Re-running it
// against a newer upstream is the drift check asks for: every local change lives
// here as a declarative transform, so a region that moved or vanished upstream fails loudly rather
// than silently reverting to the shipping ASP.NET Core behaviour.
//
// deno run -A vendor.ts [--upstream /path/to/dotnet/aspnetcore] [--check]
//
// `--check` re-renders and diffs against what is checked in instead of writing, and exits non-zero
// on any difference — that is the CI form.
//
// Three transforms, in the order they are applied:
//
// * `lean` renames the upstream `COMPONENTS` conditional-compilation symbol to `BSCF_LEAN`, which
// the project defines. Blazor already maintains a reflection-free, LinkGenerator-free selection
// of Routing behind that symbol (Components.Routing.targets — the vendoring precedent), and it
// is very nearly the selection a Worker needs; taking it costs one rename instead of dozens of
// hand-authored excisions that would then have to be re-derived on every upstream bump.
// * `rewrites` replace whole lines (matched on trimmed text, indentation preserved, every
// occurrence). This is how the `BSCF_LEAN` branch gets Blazor's *code* without Blazor's
// *identity*: its `Microsoft.AspNetCore.Components.Routing` namespaces and `internal` visibility
// are put back to the real ASP.NET Core ones, so user code type-checks against the names it
// already knows.
// * `excisions` wrap a region in `#if !BSCF_WORKERS`, for the few places upstream has no seam of
// its own. Both bounds must match, so an upstream edit inside the region is caught.

import { parseArgs } from "jsr:@std/cli@1/parse-args";
import { dirname, join } from "jsr:@std/path@1";

interface Rewrite {
  /** Verbatim upstream line, trimmed. Must occur at least once. */
  readonly from: string;
  /** Replacement line. Written at the indentation of the line it replaces. */
  readonly to: string;
}

interface Excision {
  /** Verbatim first line of the excised region, trimmed. Must be unique in the file. */
  readonly from: string;
  /** Verbatim last line of the region, trimmed. First match at or after `from`. */
  readonly to: string;
  /** Extra lines to swallow past the `to` match — closing braces, which are never unique. */
  readonly extra?: number;
  /** Why the region cannot ship to workerd. Emitted into the guard comment. */
  readonly why: string;
}

interface VendoredFile {
  /** Path under the upstream checkout root. */
  readonly upstream: string;
  /** Path under this directory. */
  readonly local: string;
  /**
   * Render a `.resx` into the `Resources` class upstream generates from it at build time, in this
   * namespace. Upstream's generator is an MSBuild task this repo does not run, and the exception
   * messages the vendored subset throws all come through it.
   */
  readonly resx?: string;
  /** Take Blazor's `COMPONENTS` selection by renaming the symbol to `BSCF_LEAN`. */
  readonly lean?: boolean;
  readonly rewrites?: readonly Rewrite[];
  readonly excisions?: readonly Excision[];
}

interface Manifest {
  readonly commit: string;
  /** Applied to every `lean` file, and silently skipped where the line is absent. */
  readonly leanRewrites: readonly Rewrite[];
  readonly files: readonly VendoredFile[];
}

const guardOpen = "#if !BSCF_WORKERS // excised: ";
const guardClose = "#endif // !BSCF_WORKERS";

function header (commit: string, upstream: string, transformed: boolean): string {
  return [
    "// <vendored/>",
    "// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.",
    "// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.",
    `//   upstream: ${upstream}`,
    `//   commit:   ${commit}`,
    transformed
      ? "//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md)."
      : "//   local changes: none — copied verbatim.",
    "// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.",
    "",
  ].join("\n");
}

function indentOf (line: string): string {
  return line.slice(0, line.length - line.trimStart().length);
}

function applyLean (lines: readonly string[]): string[] {
  return lines.map(line =>
    line.trimStart().startsWith("#")
      ? line.replace(/\bCOMPONENTS\b/g, "BSCF_LEAN")
      : line);
}

function applyRewrites (path: string, lines: readonly string[], rewrites: readonly Rewrite[], required: boolean): string[] {
  let current = [...lines];
  for (const rewrite of rewrites) {
    let hits = 0;
    current = current.map(line => {
      if (line.trim() !== rewrite.from) return line;
      hits++;
      return indentOf(line) + rewrite.to;
    });
    if (hits === 0 && required) throw new Error(`${path}: rewrite source line not found: '${rewrite.from}'`);
  }
  return current;
}

function applyExcisions (path: string, lines: readonly string[], excisions: readonly Excision[]): string[] {
  let current = [...lines];
  for (const excision of excisions) {
    const start = findUnique(path, current, excision.from);
    const end = findFrom(path, current, excision.to, start) + (excision.extra ?? 0);
    current = [
      ...current.slice(0, start),
      guardOpen + excision.why,
      ...current.slice(start, end + 1),
      guardClose,
      ...current.slice(end + 1),
    ];
  }
  return current;
}

function findUnique (path: string, lines: readonly string[], needle: string): number {
  const hits = lines.flatMap((line, index) => line.trim() === needle ? [index] : []);
  if (hits.length !== 1) throw new Error(`${path}: expected exactly one line '${needle}', found ${hits.length}`);
  return hits[0];
}

function findFrom (path: string, lines: readonly string[], needle: string, start: number): number {
  for (let index = start; index < lines.length; index++)
    if (lines[index].trim() === needle) return index;
  throw new Error(`${path}: no line '${needle}' at or after line ${start + 1}`);
}

/**
 * Minimal `.resx` → `Resources` renderer. Every entry becomes both a literal property and a
 * `Format…` overload, exactly as upstream's task emits them; C# call sites pick whichever they use
 * and ILC drops the rest, so rendering the whole file costs nothing and removes the judgement call
 * about which strings a future vendored file will reach for.
 */
function renderResources (manifest: Manifest, file: VendoredFile, source: string): string {
  const entries = [...source.matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)]
    .map(([, name, value]) => ({ name, value: decodeXml(value) }));
  const body = entries.flatMap(entry => [
    `    internal static string ${entry.name} => ${quote(entry.value)};`,
    "",
    `    internal static string Format${entry.name} (params object?[] args) =>`,
    `        string.Format(CultureInfo.CurrentCulture, ${quote(entry.value)}, args);`,
    "",
  ]);
  return [
    header(manifest.commit, file.upstream, true),
    "// Rendered from the .resx by Vendored/vendor.ts, standing in for the source-generating MSBuild",
    "// task upstream runs. One literal property and one Format overload per entry, as upstream emits.",
    "",
    "using System.Globalization;",
    "",
    `namespace ${file.resx};`,
    "",
    "internal static class Resources",
    "{",
    ...body.slice(0, -1),
    "}",
    "",
  ].join("\n");
}

function decodeXml (value: string): string {
  return value
    .replaceAll("&lt;", "<").replaceAll("&gt;", ">")
    .replaceAll("&quot;", "\"").replaceAll("&apos;", "'")
    .replaceAll("&amp;", "&");
}

function quote (value: string): string {
  return '"' + value.replaceAll("\\", "\\\\").replaceAll("\"", "\\\"").replaceAll("\n", "\\n").replaceAll("\r", "\\r") + '"';
}

function render (manifest: Manifest, file: VendoredFile, source: string): string {
  if (file.resx) return renderResources(manifest, file, source);
  let lines = source.split("\n");
  if (file.lean) lines = applyRewrites(file.local, applyLean(lines), manifest.leanRewrites, false);
  if (file.rewrites) lines = applyRewrites(file.local, lines, file.rewrites, true);
  if (file.excisions) lines = applyExcisions(file.local, lines, file.excisions);
  const transformed = Boolean(file.lean || file.rewrites || file.excisions);
  return header(manifest.commit, file.upstream, transformed) + lines.join("\n");
}

/** Renders README.md from the manifest, so the inventory can never disagree with what was copied. */
function renderReadme (manifest: Manifest): string {
  const transformed = manifest.files.filter(f => f.lean || f.rewrites || f.excisions || f.resx);
  const rows = manifest.files.map(f =>
    `| \`${f.local}\` | \`${f.upstream}\` | ${[
      f.lean && "lean",
      f.resx && "resx",
      f.rewrites && `${f.rewrites.length} rewrite(s)`,
      f.excisions && `${f.excisions.length} excision(s)`,
    ].filter(Boolean).join(", ") || "verbatim"} |`);
  const excisions = manifest.files.flatMap(f =>
    (f.excisions ?? []).map(e => `- \`${f.local}\` — from \`${e.from}\`: ${e.why}`));
  return [
    "<!-- Generated by vendor.ts. Edit manifest.json, not this file. -->",
    "# Vendored ASP.NET Core sources",
    "",
    `Copied from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) at \`${manifest.commit}\`,`,
    "MIT licensed — see `../THIRD-PARTY-NOTICES.md`.",
    "",
    "`Microsoft.AspNetCore.App` has no `browser-wasm` runtime pack, so these assemblies can never be",
    "referenced from a Worker; vendoring their source is the only way to keep the type identity user",
    "code is written against. Microsoft does the same thing to itself",
    "for Blazor, in `Components.Routing.targets`.",
    "",
    "## Regenerating",
    "",
    "```",
    "deno run -A Vendored/vendor.ts [--upstream /path/to/dotnet/aspnetcore]",
    "deno run -A Vendored/vendor.ts --check   # CI: fails if the tree drifts from the manifest",
    "```",
    "",
    "## Local changes",
    "",
    `${transformed.length} of ${manifest.files.length} files carry one. Three kinds:`,
    "",
    "- **lean** — upstream's `COMPONENTS` conditional-compilation symbol renamed to `BSCF_LEAN`,",
    "  which this project defines. Blazor maintains a reflection-free, `LinkGenerator`-free selection",
    "  of Routing behind it, and that selection is very nearly the one a Worker needs.",
    "- **rewrites** — whole-line replacements, all of them restoring identity the `BSCF_LEAN` branch",
    "  gives up: Blazor's `Microsoft.AspNetCore.Components.Routing` namespaces and `internal`",
    "  visibility become the real ASP.NET Core ones.",
    "- **excisions** — a region wrapped in `#if !BSCF_WORKERS`, for the few places upstream has no",
    "  seam of its own. Listed individually below.",
    "- **resx** — a `Resources` class rendered from a `.resx`, standing in for the source-generating",
    "  MSBuild task upstream runs.",
    "",
    "### Excisions",
    "",
    ...(excisions.length > 0 ? excisions : ["(none)"]),
    "",
    "## Inventory",
    "",
    "| Local | Upstream | Local changes |",
    "| --- | --- | --- |",
    ...rows,
    "",
  ].join("\n");
}

const args = parseArgs(Deno.args, { string: ["upstream"], boolean: ["check"] });
const here = dirname(new URL(import.meta.url).pathname);
const upstreamRoot = args.upstream ?? "/home/steve/git/github.com/dotnet/aspnetcore";
const manifest: Manifest = JSON.parse(await Deno.readTextFile(join(here, "manifest.json")));

let drifted = 0;
for (const file of manifest.files) {
  const source = await Deno.readTextFile(join(upstreamRoot, file.upstream));
  const rendered = render(manifest, file, source);
  const target = join(here, file.local);
  if (args.check) {
    const existing = await Deno.readTextFile(target).catch(() => null);
    if (existing !== rendered) { console.error(`drift: ${file.local}`); drifted++; }
    continue;
  }
  await Deno.mkdir(dirname(target), { recursive: true });
  await Deno.writeTextFile(target, rendered);
}

const readme = renderReadme(manifest);
const readmePath = join(here, "README.md");
if (args.check) {
  if (await Deno.readTextFile(readmePath).catch(() => null) !== readme) { console.error("drift: README.md"); drifted++; }
} else await Deno.writeTextFile(readmePath, readme);

if (drifted > 0) Deno.exit(1);
console.log(`${args.check ? "checked" : "vendored"} ${manifest.files.length} files @ ${manifest.commit}`);
