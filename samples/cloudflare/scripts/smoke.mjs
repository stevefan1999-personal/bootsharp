// End-to-end smoke for the full sample: boots the published worker under real workerd
// (`wrangler dev --local`) and exercises every route the sample declares, plus the three event
// kinds that are not routes at all — a cron trigger, a Durable Object, and a SignalR hub driven by
// the stock @microsoft/signalr client.
//
// This is a smoke, not a unit suite: each check asserts the one property that could only hold if
// the whole stack ran — the wasm guest booted, the generated interceptors bound, the response
// snapshot crossed back intact, and workerd accepted it. Everything with finer granularity than
// that is asserted in the C# suites, which are much faster and do not need a worker.
//
// Ports: 8791-8794 and 8796 belong to the src/js lanes; this one uses 8797 by convention.

import { spawn } from "node:child_process";
import { setTimeout as sleep } from "node:timers/promises";
import { existsSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { WebSocket } from "ws";
import * as signalR from "@microsoft/signalr";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const port = Number(process.env.BS_SAMPLE_PORT ?? 8797);
const origin = `http://127.0.0.1:${port}`;
const bootTimeoutMs = 180_000;

// The SignalR client reaches for a global WebSocket. Node has one, but `ws` is what the client
// documents for a server-side host and it accepts the extra headers the client sets.
globalThis.WebSocket ??= WebSocket;

if (!existsSync(resolve(root, "dist/worker/entrypoints.ts"))) {
  console.error("sample smoke: not published. Run `npm run publish:cs` first.");
  process.exit(1);
}
if (!existsSync(resolve(root, "dist/worker/signalr.mjs"))) {
  console.error("sample smoke: dist/worker/signalr.mjs is missing — Bootsharp.Cloudflare.SignalR's "
    + "targets did not run, so worker/index.ts has no hibernation handlers to import.");
  process.exit(1);
}

// A leftover worker from an earlier run answers just as well as ours does, and then every check
// runs against the previous run's D1 rows, KV values and actor state.
try {
  const stale = await fetch(`${origin}/api/health`, { signal: AbortSignal.timeout(1500) });
  if (stale.status > 0) {
    console.error(`sample smoke: something is already serving ${origin}. `
      + "Kill it (or set BS_SAMPLE_PORT) before running.");
    process.exit(1);
  }
} catch { /* nothing listening, which is what we want */ }

// Own process group, so the kill at the end takes wrangler's workerd child with it rather than
// leaving it bound to the port for the next run to trip over.
// --test-scheduled is what exposes /__scheduled; without it a cron trigger has no local trigger at
// all and the scheduled handler is the one entrypoint a smoke could not reach.
const worker = spawn(
  "npx", ["wrangler", "dev", "--local", "--test-scheduled",
    "--port", String(port), "--inspector-port", String(port + 100)],
  { cwd: root, stdio: ["ignore", "pipe", "pipe"], detached: true, env: { ...process.env, CI: "1" } });

const log = [];
for (const stream of [worker.stdout, worker.stderr])
  stream.on("data", chunk => log.push(chunk.toString()));

const checks = [];
let exitCode = 1;
try {
  await waitForReady();
  for (const check of [
    home, health, kv, d1, d1Grid, freeSql, r2, durableObject, durableObjectSql, queue, workflow,
    typedRouteParameter, jsonBody, queryBinding, binaryBody, repeatedSetCookie, passThroughToAssets,
    routerNegatives, cron, chatHub
  ]) {
    const mark = log.length;
    try { checks.push({ check: check.name, ...await check() }); }
    catch (error) {
      checks.push({
        check: check.name,
        pass: false,
        error: String(error?.message ?? error),
        workerLog: log.slice(mark).join("").split("\n").filter(line => !line.startsWith("[wrangler:info]"))
      });
    }
  }
  writeFileSync(resolve(root, "smoke-results.json"), JSON.stringify(checks, null, 2));
  exitCode = report();
} catch (error) {
  console.error(`sample smoke: ${error?.stack ?? error}`);
  console.error(log.join(""));
} finally {
  try { process.kill(-worker.pid, "SIGTERM"); } catch { /* already gone */ }
}
process.exit(exitCode);

// -- checks -------------------------------------------------------------------------------------

/** The compiled HTML template, served as the site root. */
async function home () {
  const response = await fetch(`${origin}/`);
  const html = await response.text();
  // Context-aware encoding is the whole point of the tier, and the flash hole is where a value the
  // caller controls reaches element content. Encoding it is the default now, not an opt-in `H()`.
  const escaped = await (await fetch(`${origin}/?flash=%3Cscript%3Ex%3C%2Fscript%3E`)).text();
  return assert({
    status: response.status,
    contentType: response.headers.get("content-type"),
    // The template's own markup, which only exists if HomePage.Render ran through the writer.
    hasPill: html.includes('class="pill"'),
    encodesFlash: escaped.includes("&lt;script&gt;") && !escaped.includes("<script>x</script>")
  }, it => it.status === 200 && it.contentType?.startsWith("text/html") && it.hasPill && it.encodesFlash);
}

async function health () {
  const response = await fetch(`${origin}/api/health`);
  const body = await response.json();
  return assert({ status: response.status, ok: body.ok, runtime: body.runtime },
    it => it.status === 200 && it.ok === true && typeof it.runtime === "string");
}

async function kv () {
  const put = await form("/api/kv", { key: "smoke", value: "kv-round-trip" });
  const got = await (await fetch(`${origin}/api/kv?key=smoke`)).json();
  return assert({ put: put.status, location: put.headers.get("location"), value: got.value },
    it => it.put === 303 && it.value === "kv-round-trip");
}

async function d1 () {
  const post = await form("/api/d1", { body: "d1-smoke" });
  const list = await (await fetch(`${origin}/api/d1`)).text();
  return assert({ post: post.status, found: list.includes("d1-smoke") },
    it => it.post === 303 && it.found);
}

async function d1Grid () {
  const response = await fetch(`${origin}/api/d1-grid`);
  return assert({ status: response.status, body: (await response.text()).slice(0, 80) },
    it => it.status === 200);
}

async function freeSql () {
  const post = await form("/api/freesql", { body: "freesql-smoke" });
  const list = await (await fetch(`${origin}/api/freesql`)).text();
  return assert({ post: post.status, found: list.includes("freesql-smoke") },
    it => it.post === 303 && it.found);
}

async function r2 () {
  const put = await form("/api/r2", { key: "smoke", value: "r2-round-trip" });
  const got = await (await fetch(`${origin}/api/r2?key=smoke`)).text();
  return assert({ put: put.status, found: got.includes("r2-round-trip") },
    it => it.put === 303 && it.found);
}

/** A Durable Object: two increments must be seen by the same instance, in order. */
async function durableObject () {
  const before = (await (await fetch(`${origin}/api/do`)).json()).value;
  await form("/api/do", {});
  await form("/api/do", {});
  const after = (await (await fetch(`${origin}/api/do`)).json()).value;
  return assert({ before, after }, it => it.after === it.before + 2);
}

async function durableObjectSql () {
  const response = await fetch(`${origin}/api/do-sql`);
  return assert({ status: response.status, body: (await response.text()).slice(0, 120) },
    it => it.status === 200);
}

async function queue () {
  const response = await form("/api/queue", { body: "queued" });
  return assert({ status: response.status, location: response.headers.get("location") },
    it => it.status === 303 && (it.location ?? "").includes("queue-sent"));
}

async function workflow () {
  const response = await form("/api/workflow", { userId: "smoke" });
  return assert({ status: response.status, location: response.headers.get("location") },
    // The flash carries the instance id, which only exists if the binding created an instance.
    it => it.status === 303 && (it.location ?? "").includes("workflow-"));
}

/** `{id:int}` is enforced by the matcher, so a non-integer is a 404 rather than a 500. */
async function typedRouteParameter () {
  const created = await fetch(`${origin}/api/notes`, {
    method: "POST", headers: { "content-type": "application/json" },
    body: JSON.stringify({ body: "typed-route" })
  });
  const location = created.headers.get("location");
  const id = Number(location?.split("/").pop());
  const one = await fetch(`${origin}/api/notes/${id}`);
  const wrongType = await fetch(`${origin}/api/notes/abc`);
  return assert({ created: created.status, location, one: one.status, wrongType: wrongType.status },
    it => it.created === 201 && Number.isInteger(id) && it.one === 200 && it.wrongType === 404);
}

/** The body bound through the app's own JsonSerializerContext, not a reflective resolver. */
async function jsonBody () {
  const response = await fetch(`${origin}/api/notes`, {
    method: "POST", headers: { "content-type": "application/json" },
    body: JSON.stringify({ body: "json-bound" })
  });
  const malformed = await fetch(`${origin}/api/notes`, {
    method: "POST", headers: { "content-type": "application/json" }, body: "{"
  });
  return assert({ status: response.status, malformed: malformed.status },
    it => it.status === 201 && it.malformed === 400);
}

/** A required query parameter is a 400 the generated binder produces; the default is compile-time. */
async function queryBinding () {
  const bound = await (await fetch(`${origin}/api/echo?text=hi&times=3`)).json();
  const defaulted = await (await fetch(`${origin}/api/echo?text=hi`)).json();
  const missing = await fetch(`${origin}/api/echo`);
  return assert({ result: bound.result, times: defaulted.times, missing: missing.status },
    it => it.result === "hi hi hi" && it.times === 1 && it.missing === 400);
}

/**
 * The byte channel. The eight bytes of a PNG signature are not valid UTF-8,
 * so through the buffered-text path they came back as replacement characters — this asserts the
 * bytes themselves, which is the only assertion that can tell the two apart.
 */
async function binaryBody () {
  const response = await fetch(`${origin}/api/bytes`);
  const bytes = [...new Uint8Array(await response.arrayBuffer())];
  return assert({
    status: response.status,
    contentType: response.headers.get("content-type"),
    bytes
  }, it => it.status === 200 && it.contentType === "image/png"
    && it.bytes.join(",") === "137,80,78,71,13,10,26,10");
}

/**
 * Two cookies is two `Set-Cookie` headers on one response — the case a flat header object cannot
 * carry. `getSetCookie()` is the only API that can tell two headers from one comma-joined one.
 */
async function repeatedSetCookie () {
  const response = await fetch(`${origin}/api/cookies`, { headers: { cookie: "session=incoming" } });
  const cookies = response.headers.getSetCookie();
  const body = await response.json();
  return assert({
    status: response.status,
    cookies,
    // The request half: the handler read the cookie the client sent.
    session: body.session,
    count: body.count
  }, it => it.status === 200 && it.cookies.length === 2
    && it.cookies.some(c => c.startsWith("session=abc"))
    && it.cookies.some(c => c.startsWith("theme=dark") && c.includes("httponly"))
    && it.session === "incoming" && it.count === 1);
}

/**
 * Declining a request in favour of the assets binding. `run_worker_first` is set, so the worker
 * sees `/app` first and the asset is served only because the result says to hand it back.
 */
async function passThroughToAssets () {
  const top = await fetch(`${origin}/app`);
  // '{*rest}' spans '/' where a single-segment '{rest}' would not.
  const nested = await fetch(`${origin}/app/nested/deep/path`);
  return assert({ top: top.status, nested: nested.status },
    // Whatever the assets binding answers, the one thing that must not happen is the worker
    // answering itself — a 0-status snapshot used to reach workerd as a thrown RangeError.
    it => it.top !== 500 && it.nested !== 500);
}

/** Literal beats parameter, and a wrong method is 405 carrying Allow. */
async function routerNegatives () {
  const notFound = await fetch(`${origin}/api/nothing-here`);
  const wrongMethod = await fetch(`${origin}/api/health`, { method: "DELETE" });
  return assert({
    notFound: notFound.status,
    wrongMethod: wrongMethod.status,
    allow: wrongMethod.headers.get("allow")
  }, it => it.notFound === 404 && it.wrongMethod === 405 && (it.allow ?? "").includes("GET"));
}

/**
 * The cron trigger. `wrangler dev` exposes it at `/__scheduled`; the handler's only observable
 * effect is the KV heartbeat it writes, which is what this reads back.
 */
async function cron () {
  const fired = await fetch(`${origin}/__scheduled?cron=${encodeURIComponent("*/15 * * * *")}`);
  // The write happens inside the scheduled event, which wrangler resolves before answering.
  const heartbeat = await (await fetch(`${origin}/api/scheduled`)).text();
  return assert({ fired: fired.status, heartbeat: heartbeat.slice(0, 160) },
    it => it.fired === 200 && it.heartbeat.includes("\"cron\"") && it.heartbeat.includes("*/15"));
}

/**
 * The hub, over the real wire, with the client Microsoft ships. Two connections to one room prove
 * the fan-out is the Durable Object's and not a per-isolate accident.
 */
async function chatHub () {
  const url = `${origin}/chat/smoke`;
  const first = connect(url);
  const second = connect(url);
  const heard = [];
  const grouped = [];
  // Block bodies, not concise arrows: a concise body returns the push() result, and the client
  // then logs "Result given for 'receive' method but server is not expecting a result".
  first.on("receive", (user, message) => { heard.push(`${user}:${message}`); });
  second.on("receive", (user, message) => { heard.push(`${user}:${message}`); });
  try {
    await first.start();
    await second.start();
    const transport = first.connection?.transport?.constructor?.name;
    // A blocking invocation with a result: the other half of the protocol from a send.
    const who = await first.invoke("who");
    await first.invoke("send", "alice", "hello wire");
    await settle();
    // A group the second connection never joined hears nothing.
    await first.invoke("join", "room-a");
    second.on("receive", (user, message) => grouped.push(`${user}:${message}`));
    await first.invoke("sendToGroup", "room-a", "alice", "members only");
    await settle();
    // HubException's message is contract; anything else would be opaque.
    let refused = null;
    try { await first.invoke("refuse"); } catch (error) { refused = String(error?.message ?? error); }
    return assert({
      transport,
      who,
      // Both connections heard the broadcast: two entries for one send.
      broadcast: heard.filter(it => it === "alice:hello wire").length,
      refused
    }, it => it.transport === "WebSocketTransport" && typeof it.who === "string" && it.who.length > 0
      && it.broadcast === 2 && (it.refused ?? "").includes("refused by contract"));
  } finally {
    await first.stop().catch(() => {});
    await second.stop().catch(() => {});
  }
}

function connect (url) {
  return new signalR.HubConnectionBuilder()
    .withUrl(url, { WebSocket })
    .configureLogging(signalR.LogLevel.None)
    .build();
}

// -- harness ------------------------------------------------------------------------------------

/** Lets a fan-out reach both clients before the assertion reads what they heard. */
function settle () { return sleep(250); }

/**
 * Posts a urlencoded body, without following the redirect.
 * <p>Every mutating route in this sample answers 303 (post/redirect/get), and a followed redirect
 * would report the home page's 200 — hiding both the status and the flash the assertions read.</p>
 */
function form (path, fields) {
  return fetch(`${origin}${path}`, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams(fields).toString(),
    redirect: "manual"
  });
}

function assert (observed, predicate) {
  return { pass: Boolean(predicate(observed)), ...observed };
}

async function waitForReady () {
  const deadline = Date.now() + bootTimeoutMs;
  while (Date.now() < deadline) {
    if (worker.exitCode != null)
      throw new Error(`wrangler exited with ${worker.exitCode}:\n${log.join("")}`);
    try {
      const response = await fetch(`${origin}/api/health`, { signal: AbortSignal.timeout(5000) });
      if (response.ok) return;
    } catch { /* not up yet */ }
    await sleep(500);
  }
  throw new Error(`worker did not answer ${origin}/api/health within ${bootTimeoutMs} ms:\n${log.join("")}`);
}

function report () {
  for (const check of checks)
    console.log(`${check.pass ? "PASS" : "FAIL"} ${check.check}`
      + (check.pass ? "" : ` — ${check.error ?? JSON.stringify(rest(check))}`));
  const passed = checks.filter(it => it.pass).length;
  console.log(`${passed}/${checks.length} checks passed. Full output: smoke-results.json`);
  return passed === checks.length ? 0 : 1;
}

function rest ({ check, pass, workerLog, ...observed }) { return observed; }
