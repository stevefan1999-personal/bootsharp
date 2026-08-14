# Bootsharp on Cloudflare Workers

C# backend compiled with **NativeAOT-LLVM** (Bootsharp), hosted as a Cloudflare Worker. The JS isolate is only a shim: it instantiates WASM once per isolate, then hands `fetch` / `queue` / Durable Object RPC / Workflow `run` to C#.

## C# entrypoints (no per-class JavaScript)

```csharp
public sealed class Counter : DurableObject<ICloudflareEnv>
{
    public Counter(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }
    public async Task<int> Get() { /* ctx.Storage */ }
    public async Task<int> Increment() { /* ctx.Storage */ }
}

public sealed class DemoWorkflow : WorkflowEntrypoint<ICloudflareEnv>
{
    public override async Task Run(WorkflowEvent evt, IWorkflowStep step) { /* step.Do / Sleep */ }
}
```

`wrangler.jsonc` `class_name` is the C# type name. The build emits `export class Counter extends DurableObject` (and the default `WorkerEntrypoint`) so workerd sees real ESM classes. C# then JSImports the live `Request` and `env` handles — product bindings are properties on `ICloudflareEnv`, not module `[Import]`s.

This matches **workers-rs** (`#[durable_object]` + wasm-bindgen) and **workers-py** (`class Counter(DurableObject)`). C# cannot use `pythonEntrypoints`, so the generator writes the named class exports.

```csharp
public sealed class Worker : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>
{
    public override async Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env)
    {
        var value = await env.KV.Get("demo"); // JSImport into workerd
        ...
    }
}
```

## What is library and what is sample

The workerd bindings, the entrypoint bases, `WorkerContext`, the structured-logging provider, the
worker runtime's JavaScript and both build-time emitters live in `src/cs/Bootsharp.Cloudflare`,
`src/cs/Bootsharp.Cloudflare.Generate` and `src/cs/Bootsharp.Cloudflare.Publish` (ADR-0007),
referenced here as projects. What stays in the sample is what is genuinely per-app:
`ICloudflareEnv` (this worker's wrangler bindings, which is why the bases are generic over it), the
`IWorker` / `IActorRuntime` export contracts, the HTTP snapshot records, and the demo's own routes,
services, SSR page and data layer.

## Worker configuration is declared, never hard-coded

Everything about *this* worker that the emitter or the JS runtime needs is an attribute in this
app, and each is declared exactly once (ADR-0007 milestone 4):

```csharp
[WorkerEnv]                                  // Env.cs — which interface is the env, under any name
public interface ICloudflareEnv { IKvNamespace KV { get; } /* … */ }

// Program.cs — routes answered from the assets binding before .NET boots.
[assembly: WorkerAssets("/app", "/_framework", "/css", "/favicon.ico")]
[assembly: Import(typeof(ILogSink))]         // binds the JS log handler at boot
```

* **Asset routes.** `WorkerAssets` prefixes are projected into the emitted module and tested by the
  packaged `isAssetPath(path, prefixes)`. There is no second copy of the predicate in C#: the two
  that existed drifted, and the JS one won every time. Declaring the attribute is also what tells
  the emitter this worker *has* an assets binding, so declare it with no prefixes to keep the
  post-guest fallback without a pre-boot fast path; omit it entirely and the module never names the
  binding at all, which is what keeps a worker with no assets type-checking against its own
  generated `Env`. Use `Binding = "…"` when wrangler's `assets.binding` is not `ASSETS`.
* **Env discovery.** `[WorkerEnv]` replaces the old match on the literal name `ICloudflareEnv`, so
  the interface may be called anything and live anywhere, and a namesake is not mistaken for it.
  Its property *names* travel into the emitted adapters, which is what lets a missing binding fail
  as `KV binding is not configured` rather than as whatever the library guessed.
* **Log sink.** The runtime binds its handler only when the app imports `ILogSink`; using
  `CloudflareJsonLoggerProvider` without that import is `CFW015` at compile time instead of a
  `TypeError` at boot.

`npm run test:generator` covers each of these, and the `CFW015`–`CFW019` diagnostics that guard
them: missing log-sink import, missing / ambiguous `[WorkerEnv]`, a binding with no adapter, and a
class whose projected JS name would collide with something the module already declares.

## How the worker module is built

Nothing under `dist/` is checked in; `dotnet publish` produces all of it, and both `npm run dev`
and `npm run deploy` publish before invoking wrangler. After a clean clone the first `npm run dev`
is therefore what creates the entry `wrangler.jsonc` points at.

```
dotnet publish
  ├─ Roslyn (Bootsharp.Cloudflare.Generate)   entrypoint projection → CFW diagnostics
  │                                           → ActorRuntime.g.cs (the C# RPC dispatch)
  ├─ NativeAOT-LLVM link                      → dist/wasm/backend.wasm
  ├─ BootsharpJS                              → dist/js/**  (Bootsharp's own ES modules)
  └─ BootsharpCloudflareWorker (this package) → dist/worker/entrypoints.ts   emitted
                                              → dist/worker/runtime.mjs      copied
                                              → dist/worker/timer-shim.mjs   copied
                                              → dist/worker/runtime.d.mts    copied
                                              → dist/worker/wasm.d.ts        copied
```

The split is ADR-0001 and ADR-0006 §1: projection and diagnostics need source locations, so they
stay in Roslyn; writing files does not, so it happens in an MSBuild task that runs after the native
link — which is what removes the build-time write into the source tree and the window in which
`wrangler deploy` could ship a module emitted from C# that is no longer the C# being deployed.
The emitted module is small (~160 lines) because it contains only what is projected from this app's
types; everything that is the same for every app is the shipped `js/runtime.mjs` asset it imports,
and its two remaining paths — the wasm binary and the Bootsharp entry — are computed by the task
from where the publish actually put them.

`npm run check:ts` type-checks the emitted module against the declarations shipped beside the
runtime asset. `npm run test:generator` drives both emitters over throwaway compilations.

## Architecture

```
Browser
  ├─ GET /            → C# SSR (NativeAOT-LLVM WASM)
  ├─ GET /app/        → static / Blazor WASM assets
  └─ /api/*           → C# Minimal-API shim
        │
        ▼
Worker JS (WorkerEntrypoint + generated DO/Workflow classes)
  instantiate Bootsharp WASM once per isolate
        │
        ▼
C# IWorker.Fetch  →  WebApplication.MapGet/Post  →  product [Import]s
C# Counter / DemoWorkflow  ←  generated extends DurableObject / WorkflowEntrypoint
```

JS is the Worker; WASM is a library. workerd has no WASI host and no threads; LLVM is `browser-wasm` + JS imports.

## Local development

```bash
cd samples/cloudflare
npm install
npx wrangler d1 migrations apply bootsharp-cf --local
npm run dev
```

The migration is a one-time step per checkout. `wrangler dev --local` starts against an empty local
D1 instance, and every D1-backed route (`/`, `/api/d1`, `/api/d1-grid`, `/api/freesql`) reads the
`notes` table that `migrations/0001_init.sql` creates — without applying it those routes fail with
`no such table: notes`.

## Logging

`ILogger<T>` is injected the usual way. The single registered provider renders each entry as one
flat JSON object — `time`, `level`, `category`, `message`, `exception` when one is attached, plus
the message template's arguments as their own fields — and hands it to JavaScript through the
`ILogSink` module import. The isolate parses it and calls `console.debug` / `info` / `warn` /
`error` according to the level.

Handing over a parsed object rather than text is the load-bearing part: Cloudflare's Workers Logs
indexes the fields of a real JS object, whereas a JSON string — or anything written to stdout from
WASM — is stored as one opaque message. The `observability` block in `wrangler.jsonc` is what keeps
those entries in Workers Logs, queryable field by field; under `wrangler dev` the same objects
print to the terminal.

## Publish

```bash
cd samples/cloudflare
npm install
npm run deploy
```

LLVM is forced (`BsLlvm=true`). Paid plan: 10 MB gzip Worker, `limits.cpu_ms` 30000.

## Size and startup (measured 2026-08-14)

The number that matters is the **deployable bundle gzip** reported by
`npx wrangler check startup` — wasm plus ~205 KiB of JS glue — not the wasm file alone.
The enforced free-plan ceiling is 3 MiB gzip (API error 10027); the paid 10 MiB figure is
documented but not encoded in tooling.

| Variant | wasm raw | wasm gzip | notes |
| --- | --- | --- | --- |
| full sample | 8,709,761 | 3,082,089 | bundle gzip **3,293,511 — 144 KB over the free ceiling**; paid plan required |
| without FreeSql | 2,044,672 | 786,564 | the ORM costs 75% of the binary |
| without data layer | 2,044,672 | 786,565 | ADO/D1 layer itself is free — FreeSql is the entire cost |
| without SSR page | 8,679,009 | 3,064,400 | SSR is ~17 KB |
| lean baseline | 1,598,716 | 638,446 | [`samples/cloudflare-minimal`](../cloudflare-minimal) as shipped — fetch + KV + structured logging, no DI container: bundle gzip **766,914**, the figure ADR-0007 budgets every layer against (it supersedes the 1,708,820 / 668,170 "fetch-only floor" estimated before the sample existed) |

Startup CPU is a non-issue: `wrangler check startup` profiles **15.1 ms active** against the
400 ms budget, because boot is lazy — the isolate startup phase only evaluates the JS shim,
and .NET instantiation happens inside the first request.

Do not bother re-sweeping ILC/trimmer feature switches: Bootsharp's release defaults already
set all of them (verified switch-by-switch; every candidate produced a byte-identical build).
Details and the attribution table live in `docs/adr/0006-toolchain-and-testing.md` §5.
