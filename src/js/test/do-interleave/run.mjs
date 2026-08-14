// PACKAGE H driver. Boots the published harness under real workerd and runs the
// scenarios that decide the SignalR dispatcher's design. Nothing is asserted about what workerd
// *should* do — each scenario reports the order it actually observed, and the verdicts are
// computed from the one monotonic trace the C# side keeps.
//
// Every scenario owns a Durable Object scope, addressed as /<scope>/<route>. That isolation is not
// cosmetic: a scenario's last handler is still resolving when the next one begins, and a shared
// actor would record it in the next scenario's trace.
//
// Ports 8791/8792/8793 are taken by the other lanes; this one uses 8794 by convention.

import { spawn } from "node:child_process";
import { setTimeout as sleep } from "node:timers/promises";
import { closeSync, existsSync, mkdtempSync, openSync, readFileSync, rmSync, unlinkSync, writeFileSync, writeSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.BS_INTERLEAVE_PORT ?? 8794);
// Production probe: BS_INTERLEAVE_ORIGIN=https://bootsharp-do-interleave-probe.<sub>.workers.dev
// skips wrangler entirely and drives the requested scenarios against that origin. Local default
// is unchanged — spawn wrangler dev --local on 8794.
const remoteOrigin = process.env.BS_INTERLEAVE_ORIGIN?.replace(/\/$/, "") || "";
const origin = remoteOrigin || `http://127.0.0.1:${port}`;
const remote = Boolean(remoteOrigin);
const bootTimeoutMs = 180_000;
const settleTimeoutMs = Number(process.env.BS_INTERLEAVE_SETTLE_MS ?? (remote ? 90_000 : 40_000));
// SignalR's keepalive frame verbatim: {"type":6} plus the 0x1E record separator. Byte-identical to
// what the harness hands setWebSocketAutoResponse, because workerd matches by exact string equality.
const PING = '{"type":6}\x1e';
const lockPath = resolve(root, ".interleave.lock");

if (!remote && !existsSync(resolve(root, "dist/worker/entrypoints.ts"))) {
  console.error("interleave harness: not published. Run src/js/scripts/interleave-test.sh.");
  process.exit(1);
}

// The port check below only sees whatever is bound to THIS port. A second run with
// BS_INTERLEAVE_PORT still shares the harness directory's default miniflare sqlite, and the two
// workerd processes step on each other's Durable Objects — the source of the spurious "Network
// connection lost" 500s. The lock is per-directory so that case fails immediately. A leftover
// lock from a dead pid is taken over; a live holder is not. Remote runs do not start workerd.
if (!remote) acquireLock();

const findings = [];
let exitCode = 1;
let persistDir;
let worker;
const log = [];
try {
  if (remote) {
    console.log(`interleave harness: remote origin ${origin} (no wrangler dev)`);
    await waitForReady();
    await runScenarios();
    writeFileSync(resolve(root, "findings.json"), JSON.stringify(findings, null, 2));
    report();
    exitCode = 0;
  } else {
    // A leftover worker from an earlier run answers /ready just as well as ours does, and then every
    // scenario runs against actors whose state carries the previous run's trace. That contaminates
    // results silently, which is worse than failing, so refuse to start on a busy port.
    let taken = false;
    try {
      const stale = await fetch(`${origin}/ready`, { signal: AbortSignal.timeout(1500) });
      taken = stale.status > 0;
    } catch { /* nothing listening, which is what we want */ }
    if (taken) {
      console.error(`interleave harness: something is already serving ${origin}. `
        + "Kill it (or set BS_INTERLEAVE_PORT) before running.");
    } else {
      // wrangler defaults persist-to to.wrangler/state in this directory. A unique tmpdir per run
      // means two workerd processes never share DO / KV sqlite, even if someone launches wrangler
      // by hand alongside the driver.
      persistDir = mkdtempSync(join(tmpdir(), "bs-interleave-"));

      // Own process group, so the kill at the end takes wrangler's workerd child with it rather than
      // leaving it bound to the port for the next run to trip over.
      worker = spawn(
        "npx",
        ["wrangler", "dev", "--local", "--port", String(port), "--inspector-port", String(port + 100),
          "--persist-to", persistDir],
        { cwd: root, stdio: ["ignore", "pipe", "pipe"], detached: true, env: { ...process.env, CI: "1" } });

      for (const stream of [worker.stdout, worker.stderr])
        stream.on("data", chunk => log.push(chunk.toString()));

      await waitForReady();
      await runScenarios();
      writeFileSync(resolve(root, "findings.json"), JSON.stringify(findings, null, 2));
      report();
      exitCode = 0;
    }
  }
} catch (error) {
  console.error(`interleave harness: ${error?.stack ?? error}`);
  if (log.length) console.error(log.join(""));
} finally {
  if (worker?.pid) {
    try { process.kill(-worker.pid, "SIGTERM"); } catch { /* already gone */ }
    await Promise.race([new Promise(done => worker.once("exit", done)), sleep(4000)]);
    try { process.kill(-worker.pid, "SIGKILL"); } catch { /* already gone */ }
  }
  if (persistDir) {
    try { rmSync(persistDir, { recursive: true, force: true }); } catch { /* already gone */ }
  }
  if (!remote) releaseLock();
}
process.exit(exitCode);

// One scenario failing must not cost the others: each is an independent observation, and a
// harness that reports nine results and one error is more useful than one that reports nothing.
async function runScenarios () {
  const only = process.env.BS_INTERLEAVE_ONLY?.split(",");
  for (const scenario of [sameSocketMicrotask, crossSocketBinding, sameSocketBinding,
    sameSocketStorage, sameSocketStorageControl, storageHandleReread, mixedStorageThenBinding,
    completionOrdering, pingAbsorber, alarmMidAwait, hibernationWake]) {
    if (only && !only.includes(scenario.name)) continue;
    const mark = log.length;
    try { findings.push(await scenario()); }
    catch (error) {
      findings.push({
        scenario: scenario.name,
        error: String(error?.message ?? error),
        workerLog: log.slice(mark).join("").split("\n").filter(line => !line.startsWith("[wrangler:info]")),
        order: []
      });
      console.error(`scenario ${scenario.name} failed: ${error?.stack ?? error}`);
    }
  }
}

// --- scenarios ---------------------------------------------------------------------------------

// Control: a continuation that never leaves the microtask queue cannot be interleaved with,
// because workerd drains microtasks inside the same JS turn that the handler was entered on
// (IoContext::runImpl's KJ_DEFER). If this one shows interleaving, nothing else in the run means
// anything.
async function sameSocketMicrotask () {
  const scope = "micro";
  const socket = await open(scope, "m");
  await send(socket, "m1|yl8");
  await send(socket, "m2|yl8");
  await settle(socket, 2);
  const trace = await snapshot(scope);
  socket.close();
  return verdict("same socket, microtask-only await (Task.Yield)", trace, "m1", "m2");
}

// The question actually asks, in its cross-connection form: two sockets, one hub
// method holding a binding call open.
async function crossSocketBinding () {
  const scope = "cross";
  const first = await open(scope, "a");
  const second = await open(scope, "b");
  await send(first, "a1|dl400");
  await sleep(120);
  await send(second, "b1|kv1");
  await settle(first, 1);
  await settle(second, 1);
  const trace = await snapshot(scope);
  first.close();
  second.close();
  return verdict("two sockets, first awaits a 400 ms host timer", trace, "a1", "b1");
}

// The same question on ONE socket, which is the case a per-connection FIFO queue would have to
// answer for: workerd's hibernation read loop is per-socket, so if it serializes anywhere, here is
// where it would.
async function sameSocketBinding () {
  const scope = "single";
  const socket = await open(scope, "s");
  await send(socket, "s1|dl400");
  await sleep(120);
  await send(socket, "s2|kv1");
  await settle(socket, 2);
  const trace = await snapshot(scope);
  socket.close();
  return verdict("one socket, first awaits a 400 ms host timer", trace, "s1", "s2");
}

// Durable Object storage is the one await workerd takes through awaitIoWithInputLock, i.e. the one
// that holds the actor input gate. The read count is calibrated so the window is wide enough to be
// a real test rather than a race the harness happened to lose, and the measured span is reported
// with the verdict so the reader can judge whether it was.
async function sameSocketStorage () {
  const scope = "storage";
  const count = await calibrateStorage();
  const socket = await open(scope, "g");
  // Deliberately many small ops rather than one big one. A loop of storage reads does not hold the
  // input gate continuously — each read holds it across its own await and drops it in between — so
  // the honest question is whether the second message gets delivered in one of those gaps. Chopping
  // the work into 30 recorded steps is what makes the answer visible: if it is delivered mid-flight,
  // js:deliver lands between two cs:resumeN entries.
  const steps = 30;
  await send(socket, `g1|${Array(steps).fill(`st${count}`).join(",")}`);
  await sleep(150);
  await send(socket, "g2|kv1");
  await settle(socket, 2);
  const trace = await snapshot(scope);
  socket.close();
  const result = verdict(`one socket, first runs ${steps} x ${count} DO storage reads`, trace, "g1", "g2");
  const entries = trace.cs.trace;
  const deliver = entries.find(e => e.event === "js:deliver" && e.detail.includes("g2"));
  const resumes = entries.filter(e => e.event.startsWith("cs:resume") && label(e) === "g1");
  result.storage = {
    reads: steps * count,
    // Where the second message landed among the first handler's recorded steps: 0 means it was not
    // delivered until the handler was done, which is what a held input gate looks like.
    resumesBeforeSecondDelivery: deliver ? resumes.filter(r => r.step < deliver.step).length : null,
    resumesTotal: resumes.length
  };
  return result;
}

// The control for the scenario above: identical shape, identical number of recorded steps, but the
// await is a host timer instead of Durable Object storage. Any difference between the two is the
// input gate and nothing else.
async function sameSocketStorageControl () {
  const scope = "storagecontrol";
  const socket = await open(scope, "c");
  const steps = 30;
  await send(socket, `c1|${Array(steps).fill("dl15").join(",")}`);
  await sleep(150);
  await send(socket, "c2|kv1");
  await settle(socket, 2);
  const trace = await snapshot(scope);
  socket.close();
  const result = verdict(`one socket, first runs ${steps} x 15 ms host timers`, trace, "c1", "c2");
  const entries = trace.cs.trace;
  const deliver = entries.find(e => e.event === "js:deliver" && e.detail.includes("c2"));
  const resumes = entries.filter(e => e.event.startsWith("cs:resume") && label(e) === "c1");
  result.storage = {
    resumesBeforeSecondDelivery: deliver ? resumes.filter(r => r.step < deliver.step).length : null,
    resumesTotal: resumes.length
  };
  return result;
}

// Not an interleaving question — a handle-lifetime one this harness found by accident, and one a
// hub's lifetime manager would hit head on. Twice, on the Ctx.Storage path, a handler died with
// "TypeError: Cannot read properties of undefined (reading 'get')", i.e. the JS registry no longer
// resolved an isolate-scoped handle id. Both programs below do the same 20,000 reads and differ
// only in whether the handle property is read once or on every iteration; neither reproduces it.
// The scenario stays as a standing A/B, and the failure stays reported as intermittent.
async function storageHandleReread () {
  const scope = "burst";
  const socket = await open(scope, "u");
  await send(socket, "u1|st20000");
  await settle(socket, 1);
  await send(socket, "u2|sp20000");
  await settle(socket, 1);
  const trace = await snapshot(scope);
  socket.close();
  const throws = trace.cs.trace.filter(entry => entry.event === "cs:throw");
  return {
    scenario: "20000 DO storage reads through a hoisted handle, then through a re-read property",
    hoistedThrew: throws.find(t => t.detail.startsWith("u1"))?.detail ?? null,
    rereadThrew: throws.find(t => t.detail.startsWith("u2"))?.detail ?? null,
    hoistedSpanMs: span(trace, "u1"),
    rereadSpanMs: span(trace, "u2"),
    order: trace.cs.trace.map(pretty)
  };
}

// The shape a real hub method has: touch storage, then await a binding. If the gate is what
// serializes, the interleave point must be the binding await and not the storage one.
async function mixedStorageThenBinding () {
  const scope = "mixed";
  const socket = await open(scope, "x");
  await send(socket, "x1|st50,dl400,st50");
  await sleep(150);
  await send(socket, "x2|kv1");
  await settle(socket, 2);
  const trace = await snapshot(scope);
  socket.close();
  return verdict("one socket, storage read then a 400 ms timer then storage read", trace, "x1", "x2");
}

// Three messages down one socket, back to back, each awaiting less than the one before. If nothing
// serializes them, they complete in reverse — which is the concrete failure a SignalR hub would
// show: completions leaving in an order the client never asked for.
async function completionOrdering () {
  const scope = "ordering";
  const socket = await open(scope, "o");
  await send(socket, "o1|dl300");
  await send(socket, "o2|dl200");
  await send(socket, "o3|dl60");
  await settle(socket, 3);
  const trace = await snapshot(scope);
  socket.close();
  const entries = trace.cs.trace;
  return {
    scenario: "three messages down one socket, awaits of 300/200/60 ms",
    deliveryOrder: entries.filter(e => e.event === "cs:enter").map(label),
    completionOrder: entries.filter(e => e.event === "cs:exit").map(label),
    ackOrder: socket.frames.filter(f => f.includes("\"ack\"")).map(f => JSON.parse(f).ack),
    peakDepth: trace.cs.peakDepth,
    order: entries.map(pretty)
  };
}

// cost model in one assertion: the SignalR ping must be answered by workerd's
// hibernation read loop without the actor — and therefore.NET — ever seeing it.
async function pingAbsorber () {
  const scope = "ping";
  const socket = await open(scope, "p");
  socket.send(PING);
  socket.send(PING);
  await settleEcho(socket, 2);
  await send(socket, "p1|kv1");
  await settle(socket, 1);
  const trace = await snapshot(scope);
  socket.close();
  const delivered = trace.cs.trace.filter(e => e.event === "js:deliver");
  return {
    scenario: "two SignalR ping frames against setWebSocketAutoResponse",
    pingsEchoed: socket.pings,
    // The absorber holds iff the only message the actor ever saw is the one that is not a ping.
    pingsDeliveredToActor: delivered.filter(e => e.detail.includes('{"type":6}')).length,
    deliveriesToActor: delivered.map(e => e.detail),
    order: trace.cs.trace.map(pretty)
  };
}

async function alarmMidAwait () {
  const scope = "alarm";
  const socket = await open(scope, "t");
  await control(scope, "/alarm?in=200&program=alarm1|kv1");
  await send(socket, "t1|dl600");
  await settle(socket, 1);
  await sleep(500);
  const trace = await snapshot(scope);
  socket.close();
  const steps = index(trace.cs.trace);
  const enter = steps["cs:enter"]?.t1;
  const exit = steps["cs:exit"]?.t1;
  const alarmEnter = trace.cs.trace.find(e => e.event === "cs:alarm-enter");
  const alarmExit = trace.cs.trace.find(e => e.event === "cs:alarm-exit");
  return {
    scenario: "DO alarm scheduled to fire while a handler awaits a 600 ms timer",
    alarmDelivered: !!alarmEnter,
    interleaved: !!(alarmEnter && enter && exit && alarmEnter.step > enter.step && alarmEnter.step < exit.step),
    peakDepth: trace.cs.peakDepth,
    detail: alarmEnter
      ? { handlerEnter: enter?.step, alarmEnter: alarmEnter.step, alarmExit: alarmExit?.step, handlerExit: exit?.step }
      : null,
    order: trace.cs.trace.map(pretty)
  };
}

async function hibernationWake () {
  // Remote repeats must not share a Durable Object with a previous run — isolate statics and the
  // scope trace would otherwise accumulate. Local keeps the historical "hibernate" name because
  // each local run starts a fresh workerd.
  const scope = process.env.BS_INTERLEAVE_SCOPE
    ?? (remote ? `hibernate-${Date.now()}` : "hibernate");
  const idleMs = Number(process.env.BS_INTERLEAVE_HIBERNATE_MS ?? 14_000);
  const socket = await open(scope, "h");
  await send(socket, "h1|kv1");
  await settle(socket, 1);
  const before = await snapshot(scope);
  // workerd's local actor container evicts after 10 s of inactivity and hibernates the accepted
  // sockets on the way out (server.c++ handleShutdown). 14 s is that plus margin; the socket is
  // deliberately idle, and the auto-response pair is never exercised here, so nothing keeps the
  // actor alive. Production may need a longer idle — set BS_INTERLEAVE_HIBERNATE_MS (70000 / 120000).
  await sleep(idleMs);
  await send(socket, "h2|kv1");
  await settle(socket, 1);
  const after = await snapshot(scope);
  socket.close();
  return {
    scenario: `hibernation wake mid-conversation (${idleMs / 1000} s idle, same socket)`,
    origin,
    scope,
    idleMs,
    actorRebuilt: after.js.incarnation > before.js.incarnation,
    incarnationBefore: before.js.incarnation,
    incarnationAfter: after.js.incarnation,
    // The C# state is isolate state. Surviving means the wake did NOT pay a.NET boot.
    isolateSurvived: after.cs.isolateBornAt === before.cs.isolateBornAt,
    conversationBefore: before.cs.conversations.h ?? null,
    conversationAfter: after.cs.conversations.h ?? null,
    socketsAfterWake: after.js.sockets,
    attachmentsAfterWake: after.js.attachments,
    csConstructions: after.cs.constructions,
    // The driver opens exactly one socket and sends exactly one message before the idle. Anything
    // above 1 here means the wake was not transparent — the socket was re-accepted, and the frame
    // that preceded the idle was delivered a second time.
    accepts: after.cs.trace.filter(e => e.event === "js:accept").length,
    deliveries: after.cs.trace.filter(e => e.event === "js:deliver").map(e => e.detail.split("msg=")[1]?.split(" ")[0]),
    order: after.cs.trace.map(pretty)
  };
}

// --- plumbing ----------------------------------------------------------------------------------

function acquireLock () {
  for (;;) {
    try {
      const fd = openSync(lockPath, "wx");
      try { writeSync(fd, `${process.pid}\n`); }
      catch (error) {
        try { unlinkSync(lockPath); } catch { /* keep the write error */ }
        throw error;
      }
      finally { closeSync(fd); }
      return;
    } catch (error) {
      if (error.code !== "EEXIST") throw error;
      const holder = lockHolderPid();
      if (holder != null && pidAlive(holder)) {
        console.error(`interleave harness: already running as pid ${holder}. `
          + "Kill it before running again — BS_INTERLEAVE_PORT does not isolate the two.");
        process.exit(1);
      }
      try { unlinkSync(lockPath); }
      catch (unlinkError) {
        if (unlinkError.code !== "ENOENT") throw unlinkError;
      }
    }
  }
}

function releaseLock () {
  try {
    if (lockHolderPid() !== process.pid) return;
    unlinkSync(lockPath);
  } catch { /* already gone, or we never held it */ }
}

function lockHolderPid () {
  try {
    const parsed = Number.parseInt(readFileSync(lockPath, "utf8"), 10);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  } catch (error) {
    if (error.code === "ENOENT") return null;
    throw error;
  }
}

// kill(pid, 0) throws ESRCH only when nothing has that pid. EPERM means the process exists but
// we cannot signal it — still a live holder.
function pidAlive (pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error.code !== "ESRCH";
  }
}

// One message whose only job is to price a storage read on this machine, so the storage scenario's
// 30 steps take about 15 ms each — the same per-step cost as its host-timer control, which is what
// makes the two directly comparable.
async function calibrateStorage () {
  const scope = "calibrate";
  const socket = await open(scope, "cal");
  await send(socket, "cal|st200");
  await settle(socket, 1);
  const trace = await snapshot(scope);
  socket.close();
  const perRead = Math.max(span(trace, "cal") ?? 1, 1) / 200;
  return Math.min(4000, Math.max(50, Math.ceil(15 / perRead)));
}

function verdict (scenario, trace, firstLabel, secondLabel) {
  const steps = index(trace.cs.trace);
  const firstEnter = steps["cs:enter"]?.[firstLabel];
  const firstExit = steps["cs:exit"]?.[firstLabel];
  const secondEnter = steps["cs:enter"]?.[secondLabel];
  const secondExit = steps["cs:exit"]?.[secondLabel];
  return {
    scenario,
    // The whole question: did the second handler start before the first one finished?
    interleaved: !!(firstEnter && firstExit && secondEnter && secondEnter.step > firstEnter.step
      && secondEnter.step < firstExit.step),
    completionOrder: [firstExit, secondExit].filter(Boolean).sort((l, r) => l.step - r.step).map(label),
    peakDepth: trace.cs.peakDepth,
    firstSpanMs: firstExit && firstEnter ? firstExit.at - firstEnter.at : null,
    jsDeliveries: trace.cs.trace.filter(e => e.event === "js:deliver").map(e => e.detail),
    order: trace.cs.trace.map(pretty)
  };
}

function span (trace, key) {
  const steps = index(trace.cs.trace);
  const enter = steps["cs:enter"]?.[key];
  const exit = steps["cs:exit"]?.[key];
  return enter && exit ? exit.at - enter.at : null;
}

function label (entry) {
  return entry.detail.split(" ")[0];
}

// event -> label -> entry, where the label is the leading token of the detail.
function index (entries) {
  const byEvent = {};
  for (const entry of entries) (byEvent[entry.event] ??= {})[label(entry)] = entry;
  return byEvent;
}

function pretty (entry) {
  return `${String(entry.step).padStart(3)} d${entry.depth} ${entry.event} ${entry.detail}`;
}

async function control (scope, path) {
  const response = await fetch(`${origin}/${scope}${path}`);
  const text = await response.text();
  if (response.status !== 200) throw Error(`/${scope}${path} answered ${response.status}: ${text}`);
  return text ? JSON.parse(text) : null;
}

// Function declarations, not consts: the scenarios run from a top-level await, i.e. before the
// module body finishes evaluating, so anything below them must be hoisted to be callable.
function snapshot (scope) { return control(scope, "/report"); }

function open (scope, conn) {
  return new Promise((done, fail) => {
    const wsOrigin = origin.replace(/^http/, "ws");
    const socket = new WebSocket(`${wsOrigin}/${scope}/ws?conn=${conn}`);
    socket.acks = 0;
    socket.pings = 0;
    socket.frames = [];
    socket.addEventListener("message", event => {
      const frame = String(event.data);
      socket.frames.push(frame);
      // Auto-response echoes arrive on the same socket; only the handler's own ack counts.
      if (frame === PING) socket.pings++;
      else if (frame.includes("\"ack\"")) socket.acks++;
    });
    socket.addEventListener("open", () => done(socket));
    socket.addEventListener("error", fail);
  });
}

async function send (socket, program) {
  socket.send(program);
}

async function settle (socket, acks) {
  const deadline = Date.now() + settleTimeoutMs;
  while (socket.acks < acks) {
    if (Date.now() > deadline) throw Error(`socket did not ack ${acks} messages (got ${socket.acks})`);
    await sleep(20);
  }
  socket.acks = 0;
}

// The auto-response reply is not an ack — no handler ran to produce it. Waiting on it separately is
// what proves the reply came from workerd rather than from the actor.
async function settleEcho (socket, pings) {
  const deadline = Date.now() + 10_000;
  while (socket.pings < pings) {
    if (Date.now() > deadline) throw Error(`socket saw ${socket.pings} ping echoes, expected ${pings}`);
    await sleep(20);
  }
}

async function waitForReady () {
  const deadline = Date.now() + bootTimeoutMs;
  while (Date.now() < deadline) {
    if (worker?.exitCode != null) throw Error(`wrangler exited with ${worker.exitCode}:\n${log.join("")}`);
    try {
      const response = await fetch(`${origin}/ready`);
      if (response.status === 200) return;
      if (remote) throw Error(`remote origin ${origin}/ready answered ${response.status}`);
    } catch (error) {
      if (remote && error instanceof Error && error.message.startsWith("remote origin")) throw error;
      /* not listening yet, or a transient remote blip before the first success */
    }
    await sleep(250);
  }
  throw Error(remote
    ? `remote origin ${origin}/ready did not answer 200 within ${bootTimeoutMs} ms`
    : `wrangler dev did not start within ${bootTimeoutMs} ms:\n${log.join("")}`);
}

function report () {
  for (const finding of findings) {
    console.log(`\n=== ${finding.scenario}`);
    for (const [key, value] of Object.entries(finding)) {
      if (key === "scenario" || key === "order") continue;
      console.log(`    ${key}: ${JSON.stringify(value)}`);
    }
    console.log("    trace:");
    for (const line of finding.order) console.log(`      ${line}`);
  }
  console.log(`\ninterleave harness: ${findings.length} scenarios recorded -> findings.json`);
}
