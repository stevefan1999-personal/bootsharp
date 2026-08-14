# PACKAGE SR — the SignalR end-to-end lane

ADR-0010's Consequences make one claim no C# unit test can check, because it is a claim about
somebody else's code: *"Stock `@microsoft/signalr` JS clients connect unmodified."* This lane checks
it, against the shipping runtime — a NativeAOT-LLVM guest inside real workerd, reached over the real
JSON hub protocol by the npm package as published.

Everything else about the hub layer is asserted deterministically in
`src/cs/Bootsharp.Cloudflare.SignalR.Test` (66 tests, a fake transport, an injected clock). This
lane deliberately owns only what that suite cannot reach.

## Ownership and placement

A lane under `src/js/test`, following the `test/do-interleave` conventions exactly — same
`Directory.Build.props`, same `nuget.config`, same `BsLlvm=true` publish, same `wrangler dev --local`
driver. Port **8796** (8791/8792/8793 are the other lanes, 8794 is do-interleave).

Unlike do-interleave, the worker module here is *almost* not hand-written. `worker/harness.ts` is
about thirty lines and every one of them is what a real app writes:

```ts
export const ChatRoomHub = hubDurableObject(ChatRoom, { sweepIntervalMs: 0 });
```

`ChatRoom` is the class the publish task emitted from the C# `HubDurableObject<ChatHub, IHarnessEnv>`
subclass; `hubDurableObject` is the shipped `js/signalr.mjs`, copied next to it by the package's
targets file. The four hibernation handlers workerd reserves — `fetch`, `webSocketMessage`,
`webSocketClose`, `webSocketError` — live in the shipped module, not here, which is the difference
between this lane and the research lane that preceded it.

## Running

```
src/cs/.scripts/llvm.sh          # once: the ILCompiler-LLVM packs
src/cs/.scripts/pack.sh          # once: the Bootsharp packages this restores
src/js/scripts/signalr-test.sh
```

`--publish-only` stops after the NativeAOT-LLVM publish. `BS_SIGNALR_PORT` moves the port,
`BS_SIGNALR_IDLE_MS` the hibernation window. Full output lands in `results.json`.

## What each scenario decides

| scenario | the claim |
| --- | --- |
| `negotiate` | the four fields `NegotiateProtocol` fixes are present, and `useStatefulReconnect` is **absent** — a client that did not ask for it rejects the whole connection (`HttpConnection.ts:358-360`) |
| `handshakeAndInvoke` | the handshake completes; `invoke` with arguments returns; arguments arrive as their declared C# types (`Add(2, 40) === 42`, not `"240"`); the slot table is case-insensitive (`invoke("echo")` reaches `Echo`) |
| `hubException` | a `HubException`'s message reaches the client verbatim — the one exception type whose message is a contract |
| `broadcast` | `Clients.All` reaches both connections of the same Durable Object, which is what "the room" means (ADR-0010 §3) |
| `groups` | group membership routes correctly, and it lives in the socket's hibernation attachment rather than in isolate state |
| `hibernationWake` | after 14 s idle the connection is still live, its connection id is unchanged, and a C# static kept counting — the socket, its attachment and the isolate all survived the wake |

## Two things this lane does not measure, on purpose

- **The alarm sweep** is disabled (`sweepIntervalMs: 0`). An alarm every 15 s would wake the actor
  during the idle window and destroy the only thing this lane can observe that unit tests cannot.
  The sweep's arithmetic — handshake timeout, client timeout, auto-response liveness — is asserted
  against an injected clock in `SweepTests`, where it is deterministic.
- **Dispatch ordering.** The per-connection FIFO is the answer to do-interleave's findings 1-3, and
  proving it needs a hub method suspended at a controlled point. That is `OrderingTests`, with real
  `TaskCompletionSource` gates; reproducing it over a socket would be a slower, flakier version of
  the same assertion.

## Result

6/6 scenarios pass. `results.json` carries the observed values, including the connection id on both
sides of the hibernation wake and the static's count before and after.
