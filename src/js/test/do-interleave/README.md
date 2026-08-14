# PACKAGE H — the Durable Object event-interleaving harness

ADR-0010 §6 names this the first SignalR milestone, gating every other piece of the work: *"DO event
interleaving under the reentrant gate (two `webSocketMessage` events arriving while a hub method
awaits a binding). The first implementation milestone is a harness proving ordered, non-interleaved
hub dispatch — before any protocol work."*

It does not prove that. It refutes it, precisely, and the shape of the refutation is what the
dispatcher has to be designed against. See **Findings** below.

## Ownership and placement

A new lane under `src/js/test`, following `test/llvm` conventions — same
`Directory.Build.props`/`nuget.config`, same `BsLlvm=true` publish, same `wrangler dev --local`
driver. It is *not* part of the llvm lane, because the llvm lane must stay green and this one is
research: it deliberately provokes failures and reports them.

Two things differ from every other lane:

- **The worker module is hand-written** (`worker/harness.ts`), not generated. The Durable Object
  generator projects RPC methods only: `webSocketMessage`, `webSocketClose` and `alarm` are on its
  reserved-name list (`Bootsharp.Cloudflare.Generate/Projection/Rules.cs:80`) and no handler slot
  maps to them. The module imports the *generated* `wrapEnv` and the shipped `runtime.mjs`, and it
  calls the guest exactly the way the generator does — `enableWorkerTimers()`, `ensureBoot()`,
  `wrapState(this.ctx)`, `wrapEnv(this.env)`, `reentrant(...)` around the guest call — so its
  results transfer to the generated shape unchanged. The one deliberate divergence: **nothing
  serializes the guest calls**, which is the question under test.
- **Every scenario owns a Durable Object scope**, addressed `/<scope>/<route>`. A scenario's last
  handler is still resolving when the next one starts; one shared actor would record it in the next
  scenario's trace.

Port **8794** (8791/8792/8793 are taken by the other lanes).

## Running

```
src/cs/.scripts/llvm.sh          # once: the ILCompiler-LLVM packs
src/cs/.scripts/pack.sh          # once: the Bootsharp packages this restores
src/js/scripts/interleave-test.sh
```

`--publish-only` stops after the NativeAOT-LLVM publish. `BS_INTERLEAVE_ONLY=name[,name]` runs a
subset; `BS_INTERLEAVE_PORT` moves the port. Full traces land in `findings.json`.

## How it measures

C# `Hub` handlers run a *program*, `label|op,op,…`, and write every interleaving point into one
static, monotonically numbered trace. JavaScript writes into the same trace through `IHub.Note`, so
delivery and dispatch are ordered against each other by step number rather than by clock. `depth` is
the number of handlers between `cs:enter` and `cs:exit` — `peakDepth > 1` *is* interleaving.

| op | await | why it is in the grammar |
| --- | --- | --- |
| `dl<ms>` | `Task.Delay` | host timer: non-storage I/O |
| `kv<n>` | n × `env.KV.Get` | the "binding call" of ADR-0010 §6; non-storage I/O |
| `st<n>` | n × `ctx.storage.get`, handle hoisted | the only API workerd awaits through `awaitIoWithInputLock` |
| `sp<n>` | same, re-reading `Ctx.Storage` each iteration | see the handle-lifetime note below |
| `yl<n>` | `Task.Yield` | stays on the microtask queue, inside one JS turn |
| `sy` | `ctx.storage.sync()` | |

## Findings

Stable across repeat runs unless noted.

1. **Two `webSocketMessage` events DO interleave while a handler awaits — on the same socket as
   well as across sockets.** `peakDepth` 2, and the second handler runs to completion inside the
   first one's await. Nothing in workerd, and nothing in bootsharp's `reentrant()` gate, serializes
   them.
2. **Completions leave in the order the awaits finish, not the order the messages arrived.** Three
   messages down one socket awaiting 300/200/60 ms arrive `o1,o2,o3` and complete — and ack —
   `o3,o2,o1`, at `peakDepth` 3.
3. **The interleave point is exactly the non-storage await.** The decisive pair: 30 steps × ~15 ms
   of Durable Object storage reads (~330 ms) delivers the second message only after all 30 steps
   (`interleaved: false`); 30 steps × 15 ms of host timers, same shape, delivers it after step 9
   (`interleaved: true`). The actor input gate is held across storage awaits and released across
   every other kind — `IoContext::awaitIoWithInputLock` has exactly one caller,
   `src/workerd/api/actor-state.c++`.
4. **A microtask-only continuation cannot be interleaved with.** `Task.Yield` stays inside the JS
   turn workerd drains in `IoContext::runImpl`'s `KJ_DEFER`.
5. **A DO alarm fires mid-await and runs to completion inside the handler's await** (`depth` 2).
   An alarm set for +200 ms against a handler awaiting 600 ms lands at step 7 of 12, between the
   handler's `cs:enter` (5) and its `cs:exit` (12), and its own `cs:alarm-exit` (9) precedes them
   both. Identical in 3 of 3 isolated runs plus every suite run. The alarm handler is therefore a
   second concurrent writer to hub state, on top of the message handlers.
6. **Hibernation wake is cheap and lossless for isolate state.** After 14 s idle (workerd's local
   container evicts at 10 s — `server.c++` `handleShutdown`), the next frame rebuilds the Durable
   Object (new incarnation, second `IHub.Construct`) but the **isolate survives**: C# statics, and
   therefore the per-connection conversation counter, continue across the wake. `getWebSockets()`
   returns the socket and `deserializeAttachment()` is intact. 3/3 identical in isolation.
   **But the wake is not always transparent:** in 1 of 4 runs the socket was *re-accepted*
   (`js:accept` twice, `sockets` 2) and the frame that preceded the idle was *delivered a second
   time* — the harness reports this explicitly as `accepts` and `deliveries`. Whether that is a
   `wrangler dev --local` proxy artifact or real at-least-once wake semantics is not decidable
   here; it is the same measurement research/10 open question 2 defers to production Cloudflare.
7. **`setWebSocketAutoResponse` absorbs the SignalR ping.** Two `{"type":6}\x1e` frames are echoed
   by workerd and produce **zero** deliveries to the actor — research/10 §9's cost model holds on a
   real DO with a wasm guest behind it.

### Handle-lifetime hazard: intermittent, and one-sided

A handler looping over `Ctx.Storage.Get` sometimes dies with
`JSException: TypeError: Cannot read properties of undefined (reading 'get')` — the JS registry no
longer resolves an *isolate-scoped* handle id. The `st`/`sp` pair is the controlled A/B: identical
20,000 reads, differing only in whether the `Ctx.Storage` property is read once or per iteration.

| path | observations | failures |
| --- | --- | --- |
| `st` — handle hoisted once | 9 | 0 |
| `sp` — property re-read per iteration | 9 | 2 |

The asymmetry is the evidence; the intermittency says the trigger is GC timing. Working hypothesis:
re-reading the property makes many C# proxies over **one** JavaScript object, the registry keys ids
by object identity so they all share one id, and the first of those proxies to be finalized disposes
that id out from under the ones still in use. `HandleScope.Isolate` does not protect against it — it
exempts the id from *invocation-scope* release (`instances.importExempt`), not from C#-initiated
disposal (`instances.disposeImported`, which also recycles the id back into the pool).

This is not an interleaving finding, but it lands on SignalR directly: a
`DurableObjectHubLifetimeManager` reaches through `Ctx.Storage` and `ctx.getWebSockets()` on every
send. Treat it as an open defect in the handle registry, not as a harness artifact — nothing in the
harness is unusual about the failing path except that it does the same thing many times.
