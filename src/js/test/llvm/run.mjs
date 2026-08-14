// The NativeAOT-LLVM end-to-end lane. Everything else in the automated suite
// publishes -c Debug, which is Mono; every mechanism relies on runs exclusively on the
// LLVM path, so this is the only place the milestone-0b capabilities are proven where they ship.
//
// It drives the already-published worker under real workerd (`wrangler dev --local`) and calls
// /probe twice, because every property under test is about an invocation boundary: what a handle
// scope releases, and what an isolate scope keeps. Assertions compare the two reports.

import { spawn } from "node:child_process";
import { setTimeout as sleep } from "node:timers/promises";
import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.BS_LLVM_PORT ?? 8793);
const origin = `http://127.0.0.1:${port}`;
const bootTimeoutMs = 120_000;

const failures = [];
const checks = [];

function check (name, condition, detail) {
  checks.push({ name, ok: !!condition, detail });
  if (!condition) failures.push(`${name}${detail == null ? "" : ` — ${detail}`}`);
}

if (!existsSync(resolve(root, "dist/worker/entrypoints.ts")))
  fail("The exercise worker is not published. Run 'npm run test:llvm' from src/js, or "
    + "'npm run publish:cs' in test/llvm.");

function fail (message) {
  console.error(`llvm lane: ${message}`);
  process.exit(1);
}

const worker = spawn(
  "npx", ["wrangler", "dev", "--local", "--port", String(port), "--inspector-port", String(port + 100)],
  { cwd: root, stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, CI: "1" } });

const log = [];
for (const stream of [worker.stdout, worker.stderr])
  stream.on("data", chunk => log.push(chunk.toString()));

let exitCode = 1;
try {
  await waitForReady();
  const first = await probe();
  const second = await probe();
  assertCapabilities(first, second);
  report();
  exitCode = failures.length === 0 ? 0 : 1;
} catch (error) {
  console.error(`llvm lane: ${error?.stack ?? error}`);
  console.error(log.join(""));
} finally {
  worker.kill("SIGTERM");
  // wrangler forwards the signal to workerd; give it a moment before the process exits anyway.
  await Promise.race([new Promise(done => worker.once("exit", done)), sleep(5000)]);
}
process.exit(exitCode);

async function waitForReady () {
  const deadline = Date.now() + bootTimeoutMs;
  while (Date.now() < deadline) {
    if (worker.exitCode != null) throw Error(`wrangler exited with ${worker.exitCode}:\n${log.join("")}`);
    try {
      const response = await fetch(`${origin}/ready`);
      // Any answer means the isolate is serving; /ready is not a route, so a 404 is a hit.
      if (response.status > 0) return;
    } catch { /* not listening yet */ }
    await sleep(250);
  }
  throw Error(`wrangler dev did not start within ${bootTimeoutMs} ms:\n${log.join("")}`);
}

async function probe () {
  const response = await fetch(`${origin}/probe`);
  const text = await response.text();
  if (response.status !== 200) throw Error(`/probe answered ${response.status}: ${text}`);
  return JSON.parse(text);
}

function assertCapabilities (first, second) {
  // (a) An awaited primitive Task<int> import: the shape the RpcInt box existed for. On the wire it
  // is a workerd JsRpcPromise reaching the int marshaler with no boxing and no rpcNumber normaliser
  // in between. Measured caveat, recorded rather than assumed: this shape passes on the LLVM path
  // with or without the await, because the CoreCLR-flavour marshaler resolves a JsRpcPromise as a
  // thenable — so this is a round-trip regression guard for the RpcInt deletion, not the proof that
  // the await is required. The shapes that do fail without it (a custom thenable, a plain value)
  // are covered on the Mono path by src/js/test/spec/interop.spec.ts.
  // Two invocations, so the value must also advance — a stale or dropped Durable Object state
  // handle would show up as a repeat or a failure rather than 1 then 2.
  check("awaited Task<int> import marshals as a number",
    typeof first.awaitedInt === "number" && Number.isInteger(first.awaitedInt),
    `got ${JSON.stringify(first.awaitedInt)} (${first.awaitedIntType})`);
  check("awaited Task<int> import round-trips across invocations",
    second.awaitedInt === first.awaitedInt + 1,
    `first=${first.awaitedInt} second=${second.awaitedInt}`);

  // (b) A handle-category object crossing JS -> C# -> JS with identity preserved: the isolate-scoped
  // KV binding. Same JavaScript object => same id => same C# proxy on the second invocation, and
  // calling through the proxy held since the first one still reaches the live binding.
  check("isolate-scoped handle keeps its identity across invocations",
    second.isolateHandleHeld && second.isolateHandleSameProxy,
    JSON.stringify({ held: second.isolateHandleHeld, same: second.isolateHandleSameProxy }));
  check("isolate-scoped handle held across invocations is still callable",
    second.isolateHandleUsable, second.isolateHandleError);

  // (c) The disposal scope actually releasing what it registered: a Durable Object stub is a fresh
  // JS object per call and carries no isolate scope, so the invocation that imported it releases it.
  // Held past that point it must fail — and the failure must come from the registry no longer
  // resolving the id, not from a workerd I/O-context error, which is what the message pins down.
  check("invocation-scoped handle is a new proxy on the next invocation",
    second.invocationHandleSameProxy === false,
    `sameProxy=${second.invocationHandleSameProxy}`);
  check("invocation-scoped handle is released when its invocation ends",
    second.staleInvocationHandleThrew, second.staleInvocationHandleError);
  check("stale handle fails because the JS registry released it",
    typeof second.staleInvocationHandleError === "string"
      && /undefined|null/i.test(second.staleInvocationHandleError),
    second.staleInvocationHandleError);

  // The lane is worthless if it silently ran on Mono.
  check("the worker under test is the NativeAOT-LLVM publish",
    typeof first.runtime === "string" && first.runtime.startsWith(".NET "), first.runtime);
}

function report () {
  for (const { name, ok, detail } of checks)
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || detail == null ? "" : ` — ${detail}`}`);
  console.log(`llvm lane: ${checks.length - failures.length}/${checks.length} checks passed`);
  if (failures.length > 0) console.error(log.join(""));
}
