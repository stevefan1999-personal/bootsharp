# Bootsharp on Cloudflare Workers — lean baseline

The smallest worker that is still a real one: **one fetch handler, one KV binding, structured
logging**, compiled to WebAssembly with NativeAOT-LLVM. No ASP.NET shim, no DI container, no ORM,
no SSR, no Durable Objects, no static assets.

Two jobs:

1. **Template.** Start here, add what you need. Everything in `backend/` is app code; everything
   the worker needs to *be* a worker comes from `Bootsharp.Cloudflare` (ADR-0007).
2. **Baseline artifact.** Its bundle is the number every layer added to the family is budgeted
   against — ADR-0007 prices size against the lean core, not against the FreeSql demo next door in
   [`samples/cloudflare`](../cloudflare).

## Measured (2026-08-14, milestone 6)

The authoritative metric is the deployable **bundle gzip** from `wrangler check startup` — wasm
plus the JS glue — not the wasm file alone (ADR-0006 §5).

| | wasm raw | wasm gzip | bundle gzip | vs. free-plan ceiling |
| --- | --- | --- | --- | --- |
| this sample | 1,600,722 | 639,356 | **768,154** (750.15 KiB) | 2,377,574 B of headroom |
| free-plan ceiling | | | 3,145,728 | enforced (API error 10027) |
| [`samples/cloudflare`](../cloudflare) | 10,205,327 | 3,590,979 | 3,821,660 | 675,932 over — paid plan, because of FreeSql |

`wrangler` reports gzip in KiB to two decimals, so every bundle-gzip byte figure here is ±6 B.

Startup: 12.9 ms active against the 400 ms budget. Boot is lazy — the isolate startup phase only
evaluates the JS shim; .NET is instantiated inside the first request.

Reproduce with `npm run size` (`wrangler check startup`), after a publish. wasm gzip is
`gzip -9`, which is what the bundler uses.

### Milestones 5 and 6 moved this sample by nothing, on purpose

`Bootsharp.Cloudflare.AspNetCore` (ADR-0008), then `.SignalR` (ADR-0010) and `.Components`
(ADR-0011) landed in the family, and this sample's bundle gzip did not move: it references none of
them, and its `Worker.Route` is still a `switch` over method and path. That is the control working.
It is worth restating why it matters — the number in the row above is what every layer's cost is a
delta *from*, so a baseline that quietly grew with each new package would make every recorded delta
a lie.

### What each layer costs, priced against this sample

Every layer was measured the same way and on the same day: clone this sample, add exactly one
layer, **use it for real** (a referenced-but-unused layer is deleted by the trimmer and would price
at zero), publish NativeAOT-LLVM, read `wrangler check startup`.

| Probe | wasm raw | wasm gzip | bundle gzip | vs. this sample |
| --- | --- | --- | --- | --- |
| this sample (control) | 1,600,722 | 639,356 | 768,154 (750.15 KiB) | — |
| \+ one plain Durable Object | 1,657,466 | 657,126 | 791,265 (772.72 KiB) | +23,111 |
| \+ `.SignalR` (hub, room DO, groups) | 2,280,408 | 895,526 | 1,033,329 (1009.11 KiB) | +265,175 |
| \+ `.AspNetCore` (routes, binding, writer page) | 2,435,377 | 976,889 | 1,112,279 (1086.21 KiB) | +344,125 |
| \+ `.AspNetCore` + `.Components` (a real `.razor`) | 2,858,866 | 1,133,897 | 1,273,835 (1243.98 KiB) | +505,681 |

Read as layer prices:

| Layer | priced on | bundle gzip |
| --- | --- | --- |
| actor dispatch (a Durable Object at all) | this sample | **+23,111 (+22.6 KiB)** |
| **`Bootsharp.Cloudflare.SignalR`** | this sample + a Durable Object | **+242,064 (+236.4 KiB)** |
| **`Bootsharp.Cloudflare.AspNetCore`** | this sample | **+344,125 (+336.1 KiB)** |
| `Bootsharp.Cloudflare.Components` (**does not run**, see below) | this sample + `.AspNetCore` | +161,556 (+157.8 KiB) |

So endpoint routing, compile-time model binding, `HttpContext`, `TypedResults` and a DI container
cost ~336 KiB; a hub with hibernation WebSockets, the real SignalR wire protocol and generated
dispatch costs ~236 KiB on top of an actor; Razor Components cost ~158 KiB on top of the Minimal
API layer they build on. An app that takes SignalR keeps ~2.01 MiB of free-plan headroom; one that
takes the Minimal API layer and Components keeps ~1.79 MiB. Deciding whether any of those trades is
worth it for a four-route worker is the point of having both samples.

The Components row is a **link-time price for a tier that does not work under NativeAOT-LLVM**. The
probe publishes, links and boots; what it cannot do is render. Inside workerd the reference-typed
fields of Microsoft's `RenderTreeFrame` read back null, so a component of nothing but static markup
renders to the empty string and answers 200 — silent, total loss — and a component with any
attribute or parameter throws `ArgumentNullException` with parameter name `key`, from the first
dictionary lookup keyed on a frame's `AttributeNameField`. `RenderTreeFrame` is
`[StructLayout(LayoutKind.Explicit)]` and overlays `String`, `Type`, `Object` and `Action<>` on the
same offsets 16/24/32; a union of *reference* types at one offset is what the wasm32 codegen does
not reproduce. The renderer itself is correct — it passes the whole
`Bootsharp.Cloudflare.Components.Test` suite on CoreCLR — so this is a toolchain defect that
nothing in this repository can fix, and the number above is recorded for the day it is fixed.

The trim audit ADR-0011 §2 gates the tier on was run anyway, on that same probe with
`TrimMode=full` and `TrimmerSingleWarn=false`. It answers the question the ADR actually asked —
the `Components.Forms` / `Validation` / `Authorization` transitive chain trims to **nothing**, and
contributes no warning. What remains is four `IL2072`s, all inside
`Microsoft.AspNetCore.Components` itself and all one pattern: `Object.GetType()` returns an
unannotated `Type` that is then passed somewhere demanding `DynamicallyAccessedMemberTypes.All` —
in `ComponentProperties.SetProperties` (twice), `ComponentFactory.PerformPropertyInjection` and
`CascadingParameterState.FindCascadingParameters`. All four are on the parameter- and
`[Inject]`-assignment paths, whose target types this package already annotates `All` at the entry
point (`ComponentRenderer.RenderAsync<TComponent>`), so the members they reach are rooted by that
annotation rather than by luck.

Two corrections to earlier figures, both from re-measuring rather than from a change in the code:
the AspNetCore layer was recorded at +380,334 B against a four-route rewrite, and reads +344,125 B
here against a three-route probe that also renders a compiled writer page — the same layer, a
slightly different app. And Components was recorded at ~238 KiB priced against a baseline that had
not itself paid for the AspNetCore layer; priced correctly against `.AspNetCore`, which it requires,
it is ~158 KiB, close to the ~152 KB research/12 originally measured.

Milestone 0b (ADR-0002 Tier-1: awaited primitive imports, the handle category, per-invocation
handle scopes) moved this sample by **+7,987 B of bundle gzip**, essentially all of it wasm
(+7,214 B wasm gzip): deterministic release adds `IDisposable` to the generated import proxies
and two members to `Bootsharp.Instances`. The full sample moved the other way (-696 B) because
deleting the `RpcInt` box also deletes its generated serializer, which this sample never had.

## The whole app

```csharp
[WorkerEnv]                                  // Env.cs — this worker's wrangler bindings, in C#
public interface IWorkerEnv
{
    IKvNamespace KV { get; }
    string ENVIRONMENT { get; }
}

// Worker.cs — wrangler's `main` default export is generated from this class.
public sealed class Worker (ILogger<Worker> logger) : WorkerEntrypoint<IWorkerEnv, WorkerResponse>, IWorker
{
    public override async Task<WorkerResponse> Fetch (IJsRequest request, IWorkerEnv env) { … }
}
```

There is no JavaScript to write. `dotnet publish` emits `dist/worker/entrypoints.ts` — an ESM
module exporting `class Worker extends WorkerEntrypoint` — and copies the packaged runtime assets
next to it. `wrangler.jsonc`'s `main` points at that emitted module, and nothing under `dist/` is
checked in.

## Run it

```bash
cd samples/cloudflare-minimal
npm install
npm run dev            # publishes C#, then wrangler dev
```

```bash
curl localhost:8787/                        # text
curl localhost:8787/api/health              # {"ok":true,"runtime":".NET 10.0.0",…}
curl -X PUT --data-raw hello localhost:8787/api/kv?key=demo
curl localhost:8787/api/kv?key=demo         # {"key":"demo","value":"hello"}
```

`wrangler dev --local` simulates KV, so the placeholder namespace id in `wrangler.jsonc` is never
read. Before deploying, create a real one and paste its id over the zeroes:

```bash
wrangler kv namespace create KV
npm run deploy
```

`wrangler.jsonc` carries no `account_id` — `wrangler deploy` takes it from `wrangler login`.

## Logging

`ILogger<T>` calls cross to JavaScript as one flat JSON object per entry and are handed to
`console.*` in the isolate, which is the only side that can produce an object Workers Logs will
index field by field. Template arguments become their own fields:

```csharp
logger.LogInformation("kv read {Key} {Found}", key, value is not null);
```

```json
{ "time": "…", "level": "information", "category": "Cloudflare.Minimal.Worker",
  "message": "kv read demo True", "Key": "demo", "Found": true }
```

The one thing this obliges you to declare is `[assembly: Import(typeof(ILogSink))]` — the JS
handler is bound to that import at boot. Forget it and you get a `CFW015` build error rather than
a `TypeError` in production.

## What to add next

| You want | Add |
| --- | --- |
| more bindings (D1, R2, Queues, Workflows) | a property on `IWorkerEnv` and the binding in `wrangler.jsonc` |
| static assets | an `assets` block in `wrangler.jsonc` and `[assembly: WorkerAssets(…)]`; without the attribute the emitted module never touches an assets binding |
| a Durable Object | a `class X : DurableObject<IWorkerEnv>`, plus the `partial class ActorRuntime : ActorRuntimeBase<IWorkerEnv>, IActorRuntime` half the generated dispatch lands in |
| dependency injection | `Bootsharp.Inject` — `services.AddBootsharp()` / `provider.RunBootsharp()` instead of the hand-wiring in `Program.cs` |
| routing, model binding, `IResult` | ADR-0008's `Bootsharp.Cloudflare.AspNetCore` layer, once it lands |

The fully-loaded worker — D1, R2, Queues, Workflows, a Durable Object, Razor SSR and an ORM — is
[`samples/cloudflare`](../cloudflare).
