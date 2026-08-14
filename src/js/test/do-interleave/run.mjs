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
import { existsSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.BS_INTERLEAVE_PORT ?? 8794);
const origin = `http://127.0.0.1:${port}`;
const bootTimeoutMs = 180_000;
// SignalR's keepalive frame verbatim: {"type":6} plus the 0x1E record separator. Byte-identical to
// what the harness hands setWebSocketAutoResponse, because workerd matches by exact string equality.
const PING = '{"type":6}\x1e';

if (!existsSync(resolve(root, "dist/worker/entrypoints.ts"))) {
  console.error("interleave harness: not published. Run src/js/scripts/interleave-test.sh.");
  process.exit(1);
}

// A leftover worker from an earlier run answers /ready just as well as ours does, and then every
// scenario runs against actors whose state carries the previous run's trace. That contaminates
// results silently, which is worse than failing, so refuse to start on a busy port.
try {
  const stale = await fetch(`${origin}/ready`, { signal: AbortSignal.timeout(1500) });
  if (stale.status > 0) {
    console.error(`interleave harness: something is already serving ${origin}. `
      + "Kill it (or set BS_INTERLEAVE_PORT) before running.");
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

const findings = [];
let exitCode = 1;
try {
  await waitForReady();
  // One scenario failing must not cost the others: each is an independent observation, and a
  // harness that reports nine results and one error is more useful than one that reports nothing.
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
  writeFileSync(resolve(root, "findings.json"), JSON.stringify(findings, null, 2));
  report();
  exitCode = 0;
} catch (error) {
  console.error(`interleave harness: ${error?.stack ?? error}`);
  console.error(log.join(""));
} finally {
  try { process.kill(-worker.pid, "SIGTERM"); } catch { /* already gone */ }
  await Promise.race([new Promise(done => worker.once("exit", done)), sleep(4000)]);
  try { process.kill(-worker.pid, "SIGKILL"); } catch { /* already gone */ }
}
process.exit(exitCode);

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
  const scope = "hibernate";
  const socket = await open(scope, "h");
  await send(socket, "h1|kv1");
  await settle(socket, 1);
  const before = await snapshot(scope);
  // workerd's local actor container evicts after 10 s of inactivity and hibernates the accepted
  // sockets on the way out (server.c++ handleShutdown). 14 s is that plus margin; the socket is
  // deliberately idle, and the auto-response pair is never exercised here, so nothing keeps the
  // actor alive.
  await sleep(14_000);
  await send(socket, "h2|kv1");
  await settle(socket, 1);
  const after = await snapshot(scope);
  socket.close();
  return {
    scenario: "hibernation wake mid-conversation (14 s idle, same socket)",
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
    const socket = new WebSocket(`ws://127.0.0.1:${port}/${scope}/ws?conn=${conn}`);
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
  const deadline = Date.now() + 40_000;
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
    if (worker.exitCode != null) throw Error(`wrangler exited with ${worker.exitCode}:\n${log.join("")}`);
    try {
      const response = await fetch(`${origin}/ready`);
      if (response.status === 200) return;
    } catch { /* not listening yet */ }
    await sleep(250);
  }
  throw Error(`wrangler dev did not start within ${bootTimeoutMs} ms:\n${log.join("")}`);
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
