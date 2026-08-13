# Bootsharp on Cloudflare Workers

C# backend compiled with **NativeAOT-LLVM** (Bootsharp), hosted as a Cloudflare Worker. The JS isolate is only a shim: it instantiates WASM once per isolate, then hands `fetch` / `queue` / Durable Object RPC / Workflow `run` to C#.

## C# entrypoints (no per-class JavaScript)

```csharp
public sealed class Counter : DurableObject
{
    public Counter(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }
    public async Task<int> Get() { /* ctx.Storage */ }
    public async Task<int> Increment() { /* ctx.Storage */ }
}

public sealed class DemoWorkflow : WorkflowEntrypoint
{
    public override async Task Run(WorkflowEvent evt, IWorkflowStep step) { /* step.Do / Sleep */ }
}
```

`wrangler.jsonc` `class_name` is the C# type name. A Roslyn source generator emits `export class Counter extends DurableObject` (and the default `WorkerEntrypoint`) so workerd sees real ESM classes. C# then JSImports the live `Request` and `env` handles — product bindings are properties on `ICloudflareEnv`, not module `[Import]`s.

This matches **workers-rs** (`#[durable_object]` + wasm-bindgen) and **workers-py** (`class Counter(DurableObject)`). C# cannot use `pythonEntrypoints`, so the generator writes the named class exports.

```csharp
public sealed class Worker : WorkerEntrypoint
{
    public override async Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env)
    {
        var value = await env.KV.Get("demo"); // JSImport into workerd
        ...
    }
}
```

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
| full sample | 8,710,087 | 3,081,467 | bundle gzip **3,291,904 — 146 KB over the free ceiling**; paid plan required |
| without FreeSql | 2,044,672 | 786,564 | the ORM costs 75% of the binary |
| without data layer | 2,044,672 | 786,565 | ADO/D1 layer itself is free — FreeSql is the entire cost |
| without SSR page | 8,679,009 | 3,064,400 | SSR is ~17 KB |
| fetch-only floor | 1,708,820 | 668,170 | minimal C# worker incl. structured logging — comfortably free-plan |

Startup CPU is a non-issue: `wrangler check startup` profiles **9.7 ms active** against the
400 ms budget, because boot is lazy — the isolate startup phase only evaluates the JS shim,
and .NET instantiation happens inside the first request.

Do not bother re-sweeping ILC/trimmer feature switches: Bootsharp's release defaults already
set all of them (verified switch-by-switch; every candidate produced a byte-identical build).
Details and the attribution table live in `docs/adr/0006-toolchain-and-testing.md` §5.
