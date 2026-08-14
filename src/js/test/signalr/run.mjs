// PACKAGE SR driver. Boots the published harness under real workerd and drives it with
// the stock @microsoft/signalr npm client — the one claim in Consequences that no unit
// test can make, because it is a claim about somebody else's client.
//
// Ports 8791/8792/8793/8794 are taken by the other lanes; this one uses 8796 by convention.

import { spawn } from "node:child_process";
import { setTimeout as sleep } from "node:timers/promises";
import { existsSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { WebSocket } from "ws";
import * as signalR from "@microsoft/signalr";

const root = dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.BS_SIGNALR_PORT ?? 8796);
const origin = `http://127.0.0.1:${port}`;
const bootTimeoutMs = 180_000;
// workerd's local container evicts an idle actor at 10 s (server.c++ handleShutdown), so 14 s is
// past the wake with margin. Measured in the do-interleave lane, reused here.
const idleMs = Number(process.env.BS_SIGNALR_IDLE_MS ?? 14_000);

// The npm client reaches for a global WebSocket; Node has one, but ws is what the client itself
// documents for a server-side host and it accepts the extra headers the client sets.
globalThis.WebSocket ??= WebSocket;

if (!existsSync(resolve(root, "dist/worker/entrypoints.ts"))) {
  console.error("signalr harness: not published. Run src/js/scripts/signalr-test.sh.");
  process.exit(1);
}
if (!existsSync(resolve(root, "dist/worker/signalr.mjs"))) {
  console.error("signalr harness: dist/worker/signalr.mjs is missing — the package's targets file "
    + "did not run. Check the Import of Bootsharp.Cloudflare.SignalR.targets in the backend csproj.");
  process.exit(1);
}

// A leftover worker from an earlier run answers /ready just as well as ours does, and then every
// scenario runs against an actor carrying the previous run's state.
try {
  const stale = await fetch(`${origin}/ready`, { signal: AbortSignal.timeout(1500) });
  if (stale.status > 0) {
    console.error(`signalr harness: something is already serving ${origin}. `
      + "Kill it (or set BS_SIGNALR_PORT) before running.");
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

const results = [];
let exitCode = 1;
try {
  await waitForReady();
  for (const scenario of [negotiate, handshakeAndInvoke, hubException, broadcast, groups, hibernationWake]) {
    const mark = log.length;
    try { results.push({ scenario: scenario.name, ...await scenario() }); }
    catch (error) {
      results.push({
        scenario: scenario.name,
        pass: false,
        error: String(error?.message ?? error),
        workerLog: log.slice(mark).join("").split("\n").filter(line => !line.startsWith("[wrangler:info]"))
      });
    }
  }
  writeFileSync(resolve(root, "results.json"), JSON.stringify(results, null, 2));
  exitCode = report();
} catch (error) {
  console.error(`signalr harness: ${error?.stack ?? error}`);
  console.error(log.join(""));
} finally {
  try { process.kill(-worker.pid, "SIGTERM"); } catch { /* already gone */ }
}
process.exit(exitCode);

// --- scenarios ---------------------------------------------------------------------------------

/** The four fields the client reads, and the one field that must never appear. */
async function negotiate () {
  const response = await fetch(`${origin}/chat/negotiate?negotiateVersion=1`, { method: "POST" });
  const body = await response.json();
  return {
    pass: body.negotiateVersion === 1
      && typeof body.connectionToken === "string"
      && body.availableTransports?.[0]?.transport === "WebSockets"
      && !("useStatefulReconnect" in body),
    body
  };
}

/**
 * The base case, and the one that exercises the most: negotiate, the handshake frame, an invocation
 * with arguments bound to their declared C# types, and a completion the client resolves.
 */
async function handshakeAndInvoke () {
  const connection = build("chat");
  await connection.start();
  try {
    const echoed = await connection.invoke("Echo", "hello wire");
    const sum = await connection.invoke("Add", 2, 40);
    const who = await connection.invoke("Who");
    const renamed = await connection.invoke("echo", "case insensitive");
    return {
      pass: echoed === "hello wire" && sum === 42 && typeof who === "string" && who.length > 0
        && renamed === "case insensitive",
      echoed, sum, who, renamed,
      transport: connection.connection?.transport?.constructor?.name ?? "unknown"
    };
  } finally { await connection.stop(); }
}

/** HubException's message is a contract; every other exception's is not. */
async function hubException () {
  const connection = build("chat");
  await connection.start();
  try {
    let message = "";
    try { await connection.invoke("Refuse"); }
    catch (error) { message = String(error?.message ?? error); }
    return { pass: message.includes("refused by contract"), message };
  } finally { await connection.stop(); }
}

/** Clients.All over one Durable Object instance, which is what "the room" means. */
async function broadcast () {
  const first = build("broadcast");
  const second = build("broadcast");
  const heard = [];
  // Braces, not a concise body: the client treats a returned value as a client result and logs
  // "Result given for 'receive' method but server is not expecting a result".
  first.on("receive", (user, message) => { heard.push(`first:${user}:${message}`); });
  second.on("receive", (user, message) => { heard.push(`second:${user}:${message}`); });
  await first.start();
  await second.start();
  try {
    await first.invoke("Broadcast", "alice", "hi everyone");
    await settle(() => heard.length >= 2);
    return { pass: heard.length === 2 && heard.every(entry => entry.endsWith("alice:hi everyone")), heard };
  } finally {
    await first.stop();
    await second.stop();
  }
}

/** Group membership lives in the socket's hibernation attachment rather than in isolate state. */
async function groups () {
  const member = build("groups");
  const outsider = build("groups");
  const heard = [];
  member.on("receive", (user, message) => { heard.push(`member:${message}`); });
  outsider.on("receive", (user, message) => { heard.push(`outsider:${message}`); });
  await member.start();
  await outsider.start();
  try {
    await member.invoke("Join", "room-a");
    await member.invoke("ToGroup", "room-a", "members only");
    await settle(() => heard.length >= 1);
    return { pass: heard.length === 1 && heard[0] === "member:members only", heard };
  } finally {
    await member.stop();
    await outsider.stop();
  }
}

/**
 * The finding this whole hosting shape rests on: a hibernation wake rebuilds the Durable Object,
 * but the socket and its attachment survive, so the connection keeps working without the client
 * noticing. `Count` is a C# static — if the number keeps climbing, the isolate survived too.
 */
async function hibernationWake () {
  const connection = build("wake");
  const closed = [];
  connection.onclose(error => { closed.push(String(error?.message ?? "clean")); });
  await connection.start();
  try {
    const before = await connection.invoke("Count");
    const idBefore = await connection.invoke("Who");
    await sleep(idleMs);
    const after = await connection.invoke("Count");
    const idAfter = await connection.invoke("Who");
    // Snapshotted here rather than handed over by reference: the stop() in the finally pushes a
    // clean close, and reporting that as if it had happened during the idle would be a lie.
    const dropped = [...closed];
    return {
      pass: dropped.length === 0 && idAfter === idBefore && after > before,
      before, after, idBefore, idAfter, dropped, idleMs
    };
  } finally { await connection.stop(); }
}

// --- plumbing ----------------------------------------------------------------------------------

function build (room) {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${origin}/${room}`)
    .configureLogging(signalR.LogLevel.Error)
    .build();
}

/** Waits for a condition a server push satisfies; a fixed sleep would be slower and flakier. */
async function settle (done, timeoutMs = 5_000) {
  const deadline = Date.now() + timeoutMs;
  while (!done() && Date.now() < deadline) await sleep(25);
}

async function waitForReady () {
  const deadline = Date.now() + bootTimeoutMs;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${origin}/ready`, { signal: AbortSignal.timeout(2000) });
      if (response.ok) return;
    } catch { /* still booting */ }
    await sleep(250);
  }
  throw new Error(`worker did not become ready in ${bootTimeoutMs} ms`);
}

function report () {
  let failed = 0;
  for (const result of results) {
    const status = result.pass ? "PASS" : "FAIL";
    if (!result.pass) failed++;
    console.log(`${status}  ${result.scenario}`);
    if (!result.pass) console.log(`      ${JSON.stringify(result, null, 2).split("\n").join("\n      ")}`);
  }
  console.log(`\n${results.length - failed}/${results.length} scenarios passed. Full output: results.json`);
  return failed === 0 ? 0 : 1;
}
