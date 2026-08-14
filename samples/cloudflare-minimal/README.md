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

## Measured (2026-08-14, milestone 0b)

The authoritative metric is the deployable **bundle gzip** from `wrangler check startup` — wasm
plus the JS glue — not the wasm file alone (ADR-0006 §5).

| | wasm raw | wasm gzip | bundle gzip | vs. free-plan ceiling |
| --- | --- | --- | --- | --- |
| this sample | 1,598,716 | 638,446 | **766,914** (748.94 KiB) | 2,378,814 B of headroom |
| free-plan ceiling | | | 3,145,728 | enforced (API error 10027) |
| [`samples/cloudflare`](../cloudflare) | 8,709,761 | 3,082,089 | 3,293,511 | 147,783 over — paid plan, because of FreeSql |

Startup: 10.7 ms active against the 400 ms budget. Boot is lazy — the isolate startup phase only
evaluates the JS shim; .NET is instantiated inside the first request.

Reproduce with `npm run size` (`wrangler check startup`), after a publish. wasm gzip is
`gzip -9`, which is what the bundler uses.

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
