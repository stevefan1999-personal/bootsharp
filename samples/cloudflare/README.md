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
referenced here as projects. The Minimal API surface — `WebApplication`, `MapGet`/`MapPost`,
`HttpContext`, `IResult`, `TypedResults`, the route matcher and the `HttpResponseData` snapshot —
comes from `src/cs/Bootsharp.Cloudflare.AspNetCore` (ADR-0008), and the interceptor that binds each
handler's parameters at compile time is a second component of `Bootsharp.Cloudflare.Generate`. What
stays in the sample is what is genuinely per-app: `ICloudflareEnv` (this worker's wrangler bindings,
which is why the bases are generic over it), the `IWorker` / `IActorRuntime` export contracts, and
the demo's own routes, services, SSR page and data layer.

## The Minimal API layer

`backend/Hosting/AspNetShim.cs` is gone. Routes are real `app.MapGet` / `app.MapPost` calls and the
handlers take the values they need rather than an `HttpContext` to dig through:

```csharp
app.MapGet("/api/notes/{id:int}", (SiteService site, int id) => site.GetNote(id));
app.MapPost("/api/notes", (SiteService site, NoteInput note) => site.CreateNote(note));
app.MapGet("/api/echo", (SiteService site, [FromQuery] string text, [FromQuery] int times = 1) =>
    site.Echo(text, times));
```

Every one of those call sites is replaced by a generated interceptor: the pattern is parsed, the
route/query/body bindings are emitted, and the precedence is computed **while the app compiles**.
Nothing reflects at runtime, which is the whole point — `RequestDelegateFactory` is
`[RequiresDynamicCode]` and cannot exist here.

What the conversion bought, none of it expressible in the shim:

* **Typed route parameters.** `{id:int}` is enforced by the matcher, so `/api/notes/abc` is a 404
  (no endpoint matched), while `/api/notes/999999` is the handler's own 404 problem document.
* **Query binding with defaults.** `/api/echo?text=hi&times=3` binds both; a missing `text` or a
  non-numeric `times` is a 400 produced before the handler is entered.
* **JSON request bodies.** `POST /api/notes` binds `NoteInput` through `ApiJsonContext` and answers
  `201 Created` with a `Location` header.
* **Correct routing.** Literal beats parameter, `405` carries an `Allow` header, and `/app/{*rest}`
  spans `/` where the shim's `{rest}` matched a single segment.
* **Bodies that are not text.** `/api/bytes` answers with the eight bytes of a PNG signature through
  `TypedResults.Bytes`. The snapshot carries a byte array, so what arrives is those eight bytes —
  through the buffered-text path they were three replacement characters.
* **Repeated response headers.** `/api/cookies` sets two cookies, which is two `Set-Cookie` headers
  on one response. Header values cross as a string *or* an array now, and the runtime appends an
  array entry by entry; comma-joining them, which is what the flat shape did, sets one malformed
  cookie.
* **Declining a request.** `/app` and `/app/{*rest}` answer with `TypedResults.PassThroughToAssets()`,
  a field of the snapshot the emitted module recognises, replacing a status code of zero that no
  `Response` can carry.

Three things this package deliberately refuses, and what the sample does instead:

* **Form binding.** `[FromForm]` is a build error and `HttpRequest.Form` throws — the urlencoded and
  multipart readers are a separate opt-in layer. The SSR page posts real HTML forms, so the six
  form handlers take an `HttpRequest` and call `FormBody.ReadAsync`, which is the package's own
  public query parser over the body text.
* **Reflective JSON.** The resolver chain starts empty. `backend/Api.cs` declares `ApiJsonContext`
  and Program.cs joins it with the familiar
  `builder.Services.ConfigureHttpJsonOptions(o => o.AddContext(ApiJsonContext.Default))`, so a type
  nobody listed fails at `app.Run()` naming the type rather than with a 500 on the first request
  that would have serialized it.
* **Auth, sessions, WebSockets, the filesystem.** Each throws
  `PlatformNotSupportedException` with a sentence naming the reason and the alternative, rather than
  degrading quietly. None of them is reachable from this sample. Cookies are no longer among them:
  `/api/cookies` reads `HttpRequest.Cookies` and answers with two `Set-Cookie` headers, which the
  response snapshot carries as a JSON array of header values.

One place the package goes *beyond* ASP.NET Core's surface: `TypedResults.RedirectSeeOther` writes
a **303**, which upstream's permanent/preserve-method matrix cannot produce. It is what every
Post/Redirect/Get form handler wants, and all seven of this sample's form posts answer with it.

Response shapes are byte-compatible with the shim's, with two documented exceptions: a redirect no
longer carries the placeholder `Redirected` body and its `text/plain` content type (only the 303 and
the `Location` were ever meaningful), and `404`/`405` from the router are now empty rather than
carrying a sentence, because they are produced by the matcher and not by a handler.

## Server-rendered HTML

`backend/Ssr/HomePage.cs` is a **compiled HTML template** (ADR-0011 §1). The authoring surface is
plain C# — a static method that takes an `HtmlWriter` and whose body is one interpolated string:

```csharp
[HtmlTemplate]
public static void Render (HtmlWriter html, HomeModel model) => html.Write($$"""
  <span class="pill">runtime {{model.Runtime}}</span>
  <pre>{{model.KvValue ?? "(empty)"}}</pre>
  {{Notice("flash", model.Flash)}}
  """);
```

There is no template language, no second file and no `.razor`. `Bootsharp.Cloudflare.Generate` reads
the markup at build time, runs an HTML tokenizer over the literal segments to work out what each
hole is from the tags around it, and replaces every call to `Render` with straight-line
`WriteLiteral` / `WriteText` / `WriteAttribute` / `WriteUrl` calls. The page costs one pass over
string literals per request.

What that fixes is not speed. The previous version was the same markup with `H(…)` around each hole:
encoding was **opt-in per hole**, `{model.Counter}` went out raw (safe only because it is an `int`),
and nothing would have caught the first forgotten call. Now:

* **Encoding is the default and the context decides it.** Element content and quoted attribute
  values are encoded; a value in an `href` is also scheme-checked, so `javascript:` becomes
  `about:invalid` — no amount of quoting would have made that safe.
* **Positions no encoder can rescue are refused at build time.** A hole inside `<script>` or
  `<style>`, in an unquoted attribute value, in an `on*` handler or in an attribute *name* is
  error `CFW031`. (`<title>` and `<textarea>` are encoded rather than refused: character references
  are decoded there, so escaping `<` is exactly what stops a value closing the element.)
* **Writing markup verbatim takes `HtmlString`**, which makes reviewing every place the page trusts
  a value one grep. `HomePage.Notice` is the sample's only use: a conditional banner is built as a
  fragment beside the template and interpolated into it.
* **Nothing depends on the generator for correctness.** A template it declines — a different body
  shape, a hole reading a private member, a call from another assembly — renders through the same
  scanner at request time and produces the same bytes, one scan slower, and says so as `CFW032`.

The result takes the template rather than its output:

```csharp
return TypedResults.Html(html => HomePage.Render(html, model));
```

That is what keeps the API streaming-shaped: `HtmlWriter` buffers today because a worker response
body is buffered, and the day a live `ReadableStream` handle can cross the interop boundary
(ADR-0007 milestone 0b) a chunk-pushing writer is a second subclass — no template and no route
changes.

Output is byte-identical to the interpolated-string version it replaces, for every value the sample
renders. The one deliberate divergence is `WebUtility.HtmlEncode`'s numeric escaping of
U+00A0–U+00FF (`é` became `&#233;`): the response states UTF-8, so those characters are written as
themselves.

## Realtime with SignalR

`backend/ChatHub.cs` is an ordinary `Hub` (ADR-0010). Nothing on it knows it is running inside a
Durable Object, and the client is the stock `@microsoft/signalr` npm package speaking the real wire
protocol — the bytes are produced by Microsoft's own `JsonHubProtocol` and `HandshakeProtocol`,
which this repository references rather than reimplements:

```csharp
public class ChatHub : Hub
{
    public Task Send (string user, string message) => Clients.All.SendAsync("receive", user, message);
    public Task Join (string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);
    public string Who () => Context.ConnectionId;
}
```

Hosting is two lines. One Durable Object **class** per hub, one **instance** per room, so
`env.CHAT.getByName("lobby")` is the room and "the server process" every `HubLifetimeManager`
semantic is defined against is that instance:

```csharp
public sealed class ChatRoom (IDurableObjectState ctx, ICloudflareEnv env)
    : HubDurableObject<ChatHub, ICloudflareEnv>(ctx, env)
{
    protected override HubDispatcher<ChatHub> CreateDispatcher () => new ChatHubDispatcher();
}
```

`ChatHubDispatcher` is generated: a case-insensitive name→slot table honouring `[HubMethodName]`,
one static parameter-type array per method feeding `IInvocationBinder`, and direct unboxed calls.
An unsupported signature — generic, `ref`, `IAsyncEnumerable` — is a build error, never a surprise
at handshake.

### The one file of JavaScript, and why it exists

`worker/index.ts` is hand-written and wrangler's `main` points at it instead of straight at the
emitted module. It adds exactly two things the emitter is structurally unable to produce:

1. **The hibernation callbacks.** workerd reserves `webSocketMessage`, `webSocketClose`,
   `webSocketError` and `alarm` as entrypoint prototype members, and the Bootsharp entrypoint
   generator refuses to project a C# method onto any reserved name. So the shipped
   `js/signalr.mjs` subclasses the *generated* Durable Object and forwards those four callbacks to
   the four ordinary RPC methods `HubDurableObject` declares (`accept`/`deliver`/`disconnect`/
   `sweep`) — which means the guest calls still take the same gated path as every other actor call.
2. **A 101 response carrying a live socket**, which the C# response snapshot (status + headers +
   body) cannot express.

Everything else falls through to `super.fetch`, which is the whole Minimal API above. A worker with
no hub keeps pointing `main` at the emitted module and writes no JavaScript at all.

### What the platform actually does, and what the dispatcher does about it

Durable Object events **interleave**: while a handler awaits anything that is not DO storage — KV,
D1, `fetch`, a timer — workerd delivers the next frame and runs its handler inside the first one's
await window, on the *same* socket as well as across sockets, and completions come back in
await-completion order rather than arrival order. That was measured, not assumed
(`src/js/test/do-interleave`), and it refuted the assumption ADR-0010 was written on.

So the layer supplies the ordering the platform does not: a **per-connection FIFO queue**, which is
also the faithful choice, because upstream's `MaximumParallelInvocationsPerClient` defaults to 1.
It is per connection and not per Durable Object — a global queue would park every client behind one
slow hub method. Two consequences worth knowing when writing a hub: the handshake ordering
requirement falls out for free, and the lifetime manager never has an `await` between a read and its
dependent write (every hibernation call — `getWebSockets`, `serializeAttachment`, `send` — is
synchronous, which is what makes that possible).

Idle connections are free. workerd's `setWebSocketAutoResponse` answers the SignalR keepalive ping
with the actor still hibernated: measured at **zero** wakes. The client-timeout and handshake-timeout
sweeps ride a Durable Object alarm instead of a timer, because a timer does not survive hibernation.

### Trying it

```bash
npm run dev
```

then, from any two clients pointed at `/chat/<room>`:

```js
const c = new signalR.HubConnectionBuilder().withUrl("http://127.0.0.1:8787/chat/lobby").build();
c.on("receive", (user, message) => console.log(user, message));
await c.start();
await c.invoke("Send", "alice", "hello room");
```

Both connections receive it; a client connected to `/chat/annex` does not, because that is a
different Durable Object. The end-to-end lane that asserts all of this against a NativeAOT-LLVM
build under real workerd — negotiate, handshake, invocation with arguments, `HubException`,
`Clients.All`, groups, and survival across a hibernation wake — is
`src/js/scripts/signalr-test.sh`.

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
  └─ /api/*           → C# Minimal API (Bootsharp.Cloudflare.AspNetCore)
        │
        ▼
Worker JS (WorkerEntrypoint + generated DO/Workflow classes)
  instantiate Bootsharp WASM once per isolate
        │
        ▼
C# IWorker.Fetch  →  WebApplication.InvokeAsync  →  MapGet/MapPost  →  product [Import]s
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
D1 instance, and every D1-backed route (`/`, `/api/d1`, `/api/d1-grid`, `/api/freesql`,
`/api/notes`, `/api/notes/{id:int}`) reads the
`notes` table that `migrations/0001_init.sql` creates — without applying it those routes fail with
`no such table: notes`.

## Smoke test

```bash
npm run publish:cs && npm run smoke
```

`scripts/smoke.mjs` boots this worker under real workerd on port 8797 and exercises **every** route
plus the three event kinds that are not routes — the cron trigger, a Durable Object, and the hub
over a WebSocket with the stock `@microsoft/signalr` client. Twenty checks; each asserts the one
property that could only hold if the whole stack ran, and the full observations land in
`smoke-results.json`.

It is deliberately not a substitute for the C# suites, which assert the same behaviours far more
finely and in milliseconds. What it covers that they structurally cannot is the crossing itself:
that the response snapshot survived the interop boundary and that workerd accepted what came back.
The four checks worth knowing by name are `binaryBody` (eight PNG-signature bytes arrive as those
bytes, not as replacement characters), `repeatedSetCookie` (two `Set-Cookie` headers, read through
`getSetCookie()` — the only API that can tell them from one comma-joined header),
`passThroughToAssets` (the worker declining a request without an illegal status code), and
`chatHub` (a broadcast heard by two connections of one room, and `HubException`'s message arriving
verbatim).

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
| full sample (milestone 6) | 10,205,327 | 3,590,979 | bundle gzip **3,821,660 (3,732.09 KiB) — 660 KB over the free ceiling**; paid plan required. The +170,844 B since milestone 5 is the SignalR layer and the chat hub |
| full sample (milestone 5) | 9,710,521 | 3,425,689 | bundle gzip 3,650,816 (3,565.25 KiB), before the hub |
| without the Minimal API layer | 8,709,761 | 3,082,089 | the same sample on the hand-written shim, measured before the ADR-0008 conversion: bundle gzip 3,293,511. The conversion costs **+357,305 bundle gzip (+348.93 KiB)** — the layer's price *net of* the ~130-line `AspNetShim` it deleted |
| without FreeSql | 2,044,672 | 786,564 | the ORM costs 75% of the binary (measured on the shim; the Minimal API delta above is additive to it) |
| without data layer | 2,044,672 | 786,565 | ADO/D1 layer itself is free — FreeSql is the entire cost |
| without SSR page | 8,679,009 | 3,064,400 | SSR is ~17 KB |
| lean baseline | 1,598,716 | 638,448 | [`samples/cloudflare-minimal`](../cloudflare-minimal) as shipped — fetch + KV + structured logging, no DI container, and deliberately **no Minimal API layer**, which is what keeps it usable as the control: bundle gzip **766,915 (748.94 KiB)**, unchanged by this milestone and the figure ADR-0007 budgets every layer against (it supersedes the 1,708,820 / 668,170 "fetch-only floor" estimated before the sample existed) |

### What the Minimal API layer costs on its own

The row above is a *net* figure: it also credits back the shim this sample deleted, and it is
measured on a binary FreeSql already dominates. To price the layer by itself, the lean baseline
was rebuilt with its four hand-routed endpoints rewritten as `app.MapGet`/`app.MapMethods` and
nothing else changed — same bindings, same responses byte for byte, same `wrangler check startup`:

| Variant | wasm raw | wasm gzip | bundle gzip |
| --- | --- | --- | --- |
| lean baseline, hand-routed | 1,598,716 | 638,448 | 766,915 (748.94 KiB) |
| lean baseline, Minimal API | 2,530,379 | 1,010,127 | 1,147,249 (1,120.36 KiB) |
| **`Bootsharp.Cloudflare.AspNetCore`** | **+931,663** | **+371,679** | **+380,334 (+371.42 KiB)** |

So the layer's standalone price is ~371 KiB of deployable bundle, and the full sample's +349 KiB
is that minus the shim's own weight. Both are well inside ADR-0007's ~2.27 MiB of headroom over
the lean core, and neither is what puts this sample over the free ceiling — FreeSql is.

### What every layer costs, re-measured at milestone 6

Each family package priced on the lean control the same way, on the same day, against the same
packs: clone [`samples/cloudflare-minimal`](../cloudflare-minimal), add one layer, **use it for
real** (a referenced-but-unused layer is deleted by the trimmer and prices at zero), publish
NativeAOT-LLVM, read `wrangler check startup`.

| Layer | priced on | bundle gzip |
| --- | --- | --- |
| actor dispatch (having a Durable Object at all) | lean control | +23,111 (+22.6 KiB) |
| **`Bootsharp.Cloudflare.SignalR`** | lean control + a Durable Object | **+242,064 (+236.4 KiB)** |
| **`Bootsharp.Cloudflare.AspNetCore`** | lean control | **+344,125 (+336.1 KiB)** |
| `Bootsharp.Cloudflare.Components` (**does not run** — see below) | lean control + `.AspNetCore` | +161,556 (+157.8 KiB) |

That last row is a link-time price for a tier that does not work yet, which is why this sample has
no `.razor` route: under NativeAOT-LLVM the reference-typed fields of Microsoft's
`RenderTreeFrame` read back null inside workerd, so a component with only static markup renders to
the **empty string** with a 200, and a component with any attribute or parameter throws
`ArgumentNullException` (parameter name `key`) from the first dictionary lookup keyed on a frame's
`AttributeNameField`. `RenderTreeFrame` is `[StructLayout(LayoutKind.Explicit)]` and overlays
`String`, `Type`, `Object` and `Action<>` fields on the shared offsets 16/24/32; that union of
reference types at one offset is what the wasm32 codegen does not reproduce. The managed logic is
fine — the same renderer passes the whole `Bootsharp.Cloudflare.Components.Test` suite on CoreCLR —
so this is a toolchain defect, not a design one, and nothing in this repository can fix it.

The full table, the absolute figures behind these deltas and two corrections to previously recorded
numbers are in the [lean control's README](../cloudflare-minimal#what-each-layer-costs-priced-against-this-sample).
The hub layer is comfortably affordable: an app taking it keeps ~2.01 MiB of free-plan headroom.

Startup CPU is a non-issue: `wrangler check startup` profiles **10.6 ms active** against the
400 ms budget, because boot is lazy — the isolate startup phase only evaluates the JS shim,
and .NET instantiation happens inside the first request. Building the endpoint table (`app.Run()`)
happens on the .NET side of that boundary, so it is inside the first request, not inside startup.

Do not bother re-sweeping ILC/trimmer feature switches: Bootsharp's release defaults already
set all of them (verified switch-by-switch; every candidate produced a byte-identical build).
Details and the attribution table live in `docs/adr/0006-toolchain-and-testing.md` §5.
