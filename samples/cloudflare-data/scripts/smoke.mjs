// End-to-end smoke for the data sample: boots the published worker under real workerd
// (`wrangler dev --local`) and drives the whole CRUD surface twice — once through the hand-written
// SQL repository, once through the FreeSql one — plus the two checks that exist only to make the
// dependency-injection claims falsifiable.
//
// Each check asserts the one property that could only hold if the whole stack ran: the wasm guest
// booted, the generated interceptors bound the parameters, a D1 statement crossed the interop
// boundary, and the response snapshot came back intact. The two that are not about CRUD are the
// important ones:
//
//   diScope         — one scope per event, shared by the endpoint and the repository beneath it,
//                     disposed when the event ends.
//   handleLifetime  — two consecutive reads both succeed. This looks redundant and is not: a
//                     captive connection (a scoped D1 connection resolved into a singleton) serves
//                     the first request and fails the second with workerd's
//                     "Cannot perform I/O on behalf of a different request". Deleting the second
//                     read deletes the only evidence that the lifetimes are right.
//
// Every check records the verbatim exchanges it made — method, path, status, and the response body
// as it came back — into `smoke-results.json` beside its verdict. The booleans say what was
// concluded; the exchanges are what a reader checks the conclusion against, without rerunning
// anything.
//
// Ports: 8791-8794, 8796 and 8797 belong to other lanes; this one uses 8798 by convention.

import { spawn } from "node:child_process";
import { setTimeout as sleep } from "node:timers/promises";
import { existsSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const port = Number(process.env.BS_SAMPLE_PORT ?? 8798);
const origin = `http://127.0.0.1:${port}`;
const bootTimeoutMs = 180_000;

if (!existsSync(resolve(root, "dist/worker/entrypoints.ts"))) {
  console.error("sample smoke: not published. Run `npm run publish:cs` first.");
  process.exit(1);
}

// A leftover worker from an earlier run answers just as well as ours does, and then every check
// runs against the previous run's rows and the previous isolate's scope counters.
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
const worker = spawn(
  "npx", ["wrangler", "dev", "--local", "--port", String(port), "--inspector-port", String(port + 100)],
  { cwd: root, stdio: ["ignore", "pipe", "pipe"], detached: true, env: { ...process.env, CI: "1" } });

const log = [];
for (const stream of [worker.stdout, worker.stderr])
  stream.on("data", chunk => log.push(chunk.toString()));

// Filled by `call` and drained by the runner, so each check carries the exchanges it made and no
// check inherits the previous one's.
let exchanges = [];

const checks = [];
let exitCode = 1;
try {
  await waitForReady();
  // Named pairs rather than bare functions: the CRUD and search checks run once per provider, so
  // their names have to say which run failed.
  for (const [name, check] of [
    ["health", health],
    ["diScope", diScope],
    ["handleLifetime", handleLifetime],
    ["crud:ado", () => crud("ado")],
    ["crud:orm", () => crud("orm")],
    ["search:ado", () => search("ado")],
    ["search:orm", () => search("orm")],
    ["bothProvidersSeeOneTable", bothProvidersSeeOneTable],
    ["routerNegatives", routerNegatives],
    ["binderNegatives", binderNegatives]
  ]) {
    const mark = log.length;
    exchanges = [];
    try { checks.push({ check: name, ...await check(), exchanges }); }
    catch (error) {
      checks.push({
        check: name,
        pass: false,
        error: String(error?.message ?? error),
        exchanges,
        workerLog: log.slice(mark).join("").split("\n").filter(line => !line.startsWith("[wrangler:info]"))
      });
    }
  }
  writeFileSync(resolve(root, "smoke-results.json"), JSON.stringify({
    // Provenance, so a stale results file cannot be mistaken for a fresh one.
    ranAt: new Date().toISOString(),
    port,
    passed: checks.filter(it => it.pass).length,
    total: checks.length,
    checks
  }, null, 2));
  exitCode = report();
} catch (error) {
  console.error(`sample smoke: ${error?.stack ?? error}`);
  console.error(log.join(""));
} finally {
  try { process.kill(-worker.pid, "SIGTERM"); } catch { /* already gone */ }
}
process.exit(exitCode);

// -- checks -------------------------------------------------------------------------------------

async function health () {
  const response = await call("GET", "/api/health");
  return assert({ status: response.status, ok: response.body.ok, runtime: response.body.runtime,
    isolateId: response.body.isolateId },
  it => it.status === 200 && it.ok === true && typeof it.isolateId === "string");
}

/**
 * The DI contract, made falsifiable.
 *
 * <p>Within one response the endpoint's scope probe and the repository's must be the same instance;
 * across two responses they must differ; the isolate id must not move; and the disposal counter
 * must advance by exactly one between them — which it can only do if the container really ran
 * `DisposeAsync` on the first event's scope.</p>
 */
async function diScope () {
  const first = (await call("GET", "/api/diag/scope")).body;
  const second = (await call("GET", "/api/diag/scope")).body;
  return assert({
    sharedWithinRequest: first.oneScopePerRequest && second.oneScopePerRequest,
    repositorySawSameScope: first.scopeId === first.repositoryScopeId,
    differsAcrossRequests: first.scopeId !== second.scopeId,
    sequenceAdvanced: second.scopeSequence === first.scopeSequence + 1,
    singletonIsStable: first.isolateId === second.isolateId,
    scopeWasDisposed: second.scopesDisposed === first.scopesDisposed + 1
  }, it => Object.values(it).every(Boolean));
}

/**
 * Two consecutive reads. See the header: this is the check a captive D1 connection fails, and it
 * fails on the second call, not the first.
 */
async function handleLifetime () {
  const first = await call("GET", "/api/ado/notes?take=1");
  const second = await call("GET", "/api/ado/notes?take=1");
  return assert({ first: first.status, second: second.status, secondIsArray: Array.isArray(second.body) },
    it => it.first === 200 && it.second === 200 && it.secondIsArray);
}

/** Create, read, list, update and delete one note through one provider. */
async function crud (provider) {
  const body = `${provider}-smoke-${Date.now()}`;
  const created = await call("POST", `/api/${provider}/notes`, { body });
  const note = created.body;
  const read = await call("GET", `/api/${provider}/notes/${note.id}`);
  const updated = await call("PUT", `/api/${provider}/notes/${note.id}`, { body: `${body}-edited` });
  // Re-read rather than trust the PUT's own response. Both providers answer the update from a
  // RETURNING clause or from the entity they just wrote, so the updated body coming back on the PUT
  // proves the statement was accepted, not that it was persisted. Only a second round trip through
  // a fresh scope and a fresh connection can tell those apart.
  const reread = await call("GET", `/api/${provider}/notes/${note.id}`);
  const deleted = await call("DELETE", `/api/${provider}/notes/${note.id}`);
  const gone = await call("GET", `/api/${provider}/notes/${note.id}`);
  return assert({
    provider,
    created: created.status,
    location: created.location,
    databaseAssignedId: Number.isInteger(note.id) && note.id > 0,
    // The insert never writes created_at: the column default does, which is the database and not C#.
    stamped: typeof note.createdAt === "string" && note.createdAt.length > 0,
    readBack: read.body.body === body,
    updatedBody: updated.body.body === `${body}-edited`,
    // The real proof the UPDATE ran: the new body survives into an independent request.
    updatePersisted: reread.body.body === `${body}-edited`,
    // Recorded, deliberately not asserted as a strict increase. SQLite's datetime('now') has
    // one-second resolution and this script creates and updates well inside one second, so the two
    // stamps are normally equal and a `>` here would fail on a fast machine and "pass" only on a
    // slow one. `created_at` not moving is the property that is actually always true.
    updatedAtBefore: note.updatedAt,
    updatedAtAfter: updated.body.updatedAt,
    createdAtHeld: updated.body.createdAt === note.createdAt,
    deleted: deleted.status,
    gone: gone.status
  }, it => it.created === 201 && it.location === `/api/${provider}/notes/${note.id}`
    && it.databaseAssignedId && it.stamped && it.readBack && it.updatedBody && it.updatePersisted
    && it.createdAtHeld && it.deleted === 204 && it.gone === 404);
}

/** The search endpoint, whose `q` is required and whose window is clamped. */
async function search (provider) {
  const body = `${provider}-searchable-${Date.now()}`;
  const note = (await call("POST", `/api/${provider}/notes`, { body })).body;
  const hits = await call("GET", `/api/${provider}/notes/search?q=${encodeURIComponent(body)}`);
  const clamped = await call("GET", `/api/${provider}/notes/search?q=x&take=99999`);
  await call("DELETE", `/api/${provider}/notes/${note.id}`);
  return assert({
    provider,
    found: Array.isArray(hits.body) && hits.body.some(hit => hit.id === note.id),
    // "/search" is a literal segment and "{id:int}" a parameter one; precedence, not registration
    // order, is what routes this here. Had the parameter won, "search" would not parse as an int
    // and the response would be the matcher's 404 rather than a list.
    literalBeatsParameter: Array.isArray(hits.body),
    clamped: clamped.status
  }, it => it.found && it.literalBeatsParameter && it.clamped === 200);
}

/** One table, two providers: what the ORM wrote is what the hand-written SQL reads back. */
async function bothProvidersSeeOneTable () {
  const body = `crossed-${Date.now()}`;
  const written = (await call("POST", "/api/orm/notes", { body })).body;
  const read = await call("GET", `/api/ado/notes/${written.id}`);
  await call("DELETE", `/api/ado/notes/${written.id}`);
  return assert({ id: written.id, body: read.body.body, createdAt: read.body.createdAt === written.createdAt },
    it => it.body === body && it.createdAt);
}

/** What the compile-time route table refuses, refused at runtime by the matcher. */
async function routerNegatives () {
  const notAnInt = await call("GET", "/api/ado/notes/abc");
  const noSuchRoute = await call("GET", "/api/nope");
  const wrongMethod = await call("PATCH", "/api/ado/notes/1");
  return assert({
    notAnInt: notAnInt.status,
    noSuchRoute: noSuchRoute.status,
    wrongMethod: wrongMethod.status,
    allow: wrongMethod.allow
  }, it => it.notAnInt === 404 && it.noSuchRoute === 404 && it.wrongMethod === 405
    && it.allow?.includes("GET"));
}

/** What the generated binder and the handlers refuse before any SQL is prepared. */
async function binderNegatives () {
  const missingQuery = await call("GET", "/api/ado/notes/search");
  const emptyBody = await call("POST", "/api/ado/notes", { body: "   " });
  return assert({ missingQuery: missingQuery.status, emptyBody: emptyBody.status },
    it => it.missingQuery === 400 && it.emptyBody === 400);
}

// -- helpers ------------------------------------------------------------------------------------

/**
 * One HTTP exchange, recorded as it happens.
 *
 * <p>The record is the point: a boolean saying "readBack" tells a reader what this script decided,
 * and the body beside it is what the worker actually said. Bodies are kept verbatim — parsed when
 * the response is JSON, raw text otherwise, and omitted entirely for the empty ones — so
 * `smoke-results.json` can be read as evidence rather than as a claim.</p>
 */
async function call (method, path, payload) {
  const response = await fetch(`${origin}${path}`, {
    method,
    ...payload === undefined ? {} : {
      headers: { "content-type": "application/json" },
      body: JSON.stringify(payload)
    }
  });
  const text = await response.text();
  const body = parse(text);
  const location = response.headers.get("location");
  const allow = response.headers.get("allow");
  exchanges.push({
    request: `${method} ${path}`,
    ...payload === undefined ? {} : { requestBody: payload },
    status: response.status,
    ...location === null ? {} : { location },
    ...allow === null ? {} : { allow },
    ...text === "" ? {} : { response: body }
  });
  return { status: response.status, location, allow, body, text };
}

function parse (text) {
  try { return JSON.parse(text); }
  catch { return text; }
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

function rest ({ check, pass, workerLog, exchanges, ...observed }) { return observed; }
