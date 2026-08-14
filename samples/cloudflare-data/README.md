# Bootsharp on Cloudflare Workers — dependency injection over D1

An ASP.NET Core Minimal API on [`Bootsharp.Cloudflare.AspNetCore`](../../src/cs/Bootsharp.Cloudflare.AspNetCore)
with a **real dependency-injection graph** — interfaces at every boundary, constructor injection,
`ILogger<T>` where the events happen — reading and writing **Cloudflare D1** through an ADO.NET
provider, twice: once through hand-written SQL, and once through the **FreeSql** ORM.

The two implementations are the point. `/api/ado/notes*` and `/api/orm/notes*` are twelve endpoints
over one `INoteRepository` contract, and the handler bodies beneath them are shared verbatim — so
the only difference between an endpoint served by SQL you can read and one served by an ORM is
which interface its lambda asks for.

It is a separate sample from [`samples/cloudflare`](../cloudflare) and
[`samples/cloudflare-minimal`](../cloudflare-minimal) on purpose. The lean sample's invariant is
that its bundle never moves — every layer in ADR-0007 is priced as a delta from it — and the full
sample is already 20+ routes over the free-plan ceiling, so neither can host a DI-and-ORM story
legibly. This one can, and it is priced on its own below.

## What it demonstrates

* **A DI graph that is correct for the isolate model**, not merely present: every lifetime here is
  decided by one question, and the [table below](#the-di-registration-table) records the answer and
  the attribute that settles it.
* **A route that proves it.** `GET /api/diag/scope` reports scope identity, sharing and disposal, so
  the claims in this README are falsifiable rather than assertions.
* **Two data layers under one contract.** Hand-written SQL over `System.Data.Common`, and FreeSql
  over the same connection factory.
* **The Minimal API layer doing real work**: typed route parameters, required and defaulted query
  binding, JSON request bodies through a source-generated context, `201 Created` with `Location`,
  `204 No Content`, `400`s the binder produces before a handler is entered, and `404`/`405` from the
  matcher.

## Run it

```bash
cd samples/cloudflare-data
npm install
npx wrangler d1 migrations apply bootsharp-cf-data --local   # one time, per checkout
npm run dev
```

The migration is **one-time per checkout** and not optional: `wrangler dev --local` starts against
an empty local D1 instance, and every route below reads the `notes` table that
`migrations/0001_init.sql` creates. Without it, the first request fails with `no such table: notes`.
`npm run migrate:local` is the same command under a shorter name.

`npm run dev` publishes the C# first — nothing under `dist/` is checked in, and wrangler's `main`
points at `dist/worker/entrypoints.ts`, which `dotnet publish` emits.

```bash
curl 127.0.0.1:8787/api/health
curl -X POST 127.0.0.1:8787/api/ado/notes -H 'content-type: application/json' -d '{"body":"hello"}'
curl 127.0.0.1:8787/api/orm/notes        # the ORM reads what the SQL wrote: one table
curl 127.0.0.1:8787/api/diag/scope
```

### Deploying

`wrangler.jsonc` carries **placeholder resource ids** — no account id, and a zeroed `database_id`,
each with a comment saying what to replace it with. `wrangler dev --local` never reads them.
Before `npm run deploy`:

```bash
npx wrangler d1 create bootsharp-cf-data          # paste the id over the zeroes
npx wrangler d1 migrations apply bootsharp-cf-data --remote
```

This sample needs a **paid plan** (see [Size](#size-and-startup-measured-2026-08-14)): FreeSql puts
it over the enforced 3 MiB free-plan ceiling. Without the ORM it fits the free plan with room to
spare, and that is measured, not estimated.

## The layering

```
Routes.cs            twelve endpoints, two prefixes, one set of handler bodies
      │                (handler bodies take INoteRepository and nothing else)
      ▼
INoteRepository      the contract — no SQL, no D1, no ADO.NET in its vocabulary
      ├── AdoNoteRepository       hand-written SQL over System.Data.Common.DbConnection
      └── FreeSqlNoteRepository   the same contract, through IFreeSql
      ▼
D1DbConnection       ADO.NET facade over the D1 binding (Data/**, sample-owned)
      ▼
ICloudflareEnv.DB    the wrangler binding, as an interop handle
```

Two sub-interfaces, `IAdoNoteRepository` and `IOrmNoteRepository`, exist only because this sample
hosts both implementations at once and a handler parameter's *declared type* is what the compile-time
binder resolves. An app that picks one registers `INoteRepository` and never declares them.

## The DI registration table

Every lifetime below answers one question: **does this service reach a JavaScript handle whose id
the runtime releases when the event ends?** If yes, it is scoped, without exception. Nothing here is
a matter of taste; each row cites what settles it.

| Service | Lifetime | Why |
| --- | --- | --- |
| `IIsolateProbe` | **Singleton** | Holds no handle at all — a guid and two counters. The only kind of service that may be a singleton in a worker. |
| `IWorker` | **Singleton** | The entrypoint itself. It receives the per-event `env` as a *parameter* and writes it to the ambient slot rather than capturing it, which is what keeps a singleton legal. |
| `ILoggerFactory`, `ILoggerProvider`, `ILogger<>` | **Singleton** | The log sink is a Bootsharp module import, bound once per isolate. |
| `IScopeProbe` | **Scoped** | Must be, or `/api/diag/scope` could not tell one event from another. `IAsyncDisposable`, so its disposal is countable. |
| `ICloudflareEnv` | **Scoped** | The one ambient read in the app (`_ => WorkerContext.Env`), re-taken per event. Correct whether the env handle is the isolate-memoized one or a fresh per-event handle — ADR-0005 §3 admits both. Resolving it outside a worker event throws where the scope is built, which is a loud failure at a known place. |
| `DbConnection` | **Scoped** | `ID1Database` is `[JSHandle(Scope = HandleScope.Isolate)]` and would be legal to hold forever — but `D1DbConnection.Open()` calls `withSession`, and `ID1DatabaseSession` carries **no** `[JSHandle]`, so it defaults to `HandleScope.Invocation` and its id is released when the event ends. Same for the `ID1PreparedStatement` every command prepares. |
| `IAdoNoteRepository` | **Scoped** | Depends on `DbConnection`. A scoped dependency forces a scoped consumer. |
| `IFreeSql` | **Scoped** | FreeSql's `UseConnectionFactory` feeds an *internal ADO pool*, so a singleton would hand event N+1 a pooled connection carrying event N's released session handle. Disposed with the scope, because `IFreeSql` is `IDisposable` — the per-handler `using var fsql = …` of the older sample, with ownership moved to the container. |
| `IOrmNoteRepository` | **Scoped** | Depends on `IFreeSql`. |

The registrations live in [`DataServiceCollectionExtensions.cs`](backend/DataServiceCollectionExtensions.cs),
one extension per layer, and `Program.cs` reads top to bottom: logging, JSON, data, entrypoint.

### What a wrong lifetime looks like

A singleton capturing the scoped `DbConnection` **serves the first request correctly** and fails the
second with workerd's `Cannot perform I/O on behalf of a different request`. `Close()` cannot rescue
it: it only drops the C# reference, and nothing can un-release a handle id.

That failure mode is why `handleLifetime` in `scripts/smoke.mjs` reads twice and asserts both. It
looks redundant. Deleting the second read deletes the only evidence that the lifetimes are right.

> **Worth knowing before you copy this graph.** `WebApplicationBuilder.Build()` calls
> `Services.BuildServiceProvider()` with no `ServiceProviderOptions`, so **`ValidateScopes` and
> `ValidateOnBuild` are both off**. A captive scoped dependency therefore resolves silently from the
> root provider instead of throwing, and surfaces as the cross-request I/O error above — one layer
> away from its cause. Until the layer offers a `Build(ServiceProviderOptions)` overload, a route
> like `/api/diag/scope` plus a two-read smoke check is what stands in for scope validation.

### `ILogger<T>` goes in constructors, not in handler lambdas

Every repository here takes `ILogger<T>` in its constructor. That is good design anyway — the
endpoint is not where the interesting events happen — but on this platform it is also the only shape
that works.

The Minimal API generator answers "is this handler parameter a service?" by reading
`Add{Singleton,Scoped,Transient}` call sites at compile time (ADR-0008 §2). The canonical logging
registration is the **unbound** generic `AddSingleton(typeof(ILogger<>), typeof(Logger<>))`, and the
scan compares *closed* type names — so a parameter typed `ILogger<Foo>` never matches it. On a `GET`
that is a `CFW026` build error; on a `POST` or `PUT` it degrades to body binding and surfaces as a
confusing `CFW029` warning naming `ILogger<Foo>`. The documented hatch, if you really want a logger
on a lambda, is `[FromServices]`.

The same scan is why the registration extensions in this sample are still visible to it: the
`AddScoped<…>` call sites are compiled *here*. A registration made inside a **referenced assembly**
is invisible by design.

## Proving the DI scope

```console
$ curl -s 127.0.0.1:8787/api/diag/scope
{"scopeId":"a0fd38f4-…","repositoryScopeId":"a0fd38f4-…","oneScopePerRequest":true,
 "scopeSequence":1,"isolateId":"a1eac952-…","scopesDisposed":0}

$ curl -s 127.0.0.1:8787/api/diag/scope
{"scopeId":"f9902f48-…","repositoryScopeId":"f9902f48-…","oneScopePerRequest":true,
 "scopeSequence":2,"isolateId":"a1eac952-…","scopesDisposed":1}
```

Four separate claims, each falsifiable from those two responses:

1. **One scope per event, shared down the graph.** `scopeId` equals `repositoryScopeId`: the probe
   the endpoint was handed and the probe the repository's constructor was handed are the same
   instance. A service accidentally resolved from the root provider is what makes these differ.
2. **A new scope per event.** The two `scopeId`s differ and `scopeSequence` advances.
3. **The singleton really is one.** `isolateId` does not move.
4. **The scope is disposed, not merely abandoned.** `scopesDisposed` reads N-1 on the Nth request,
   because the response is built before the event's own scope is disposed. It can only advance if
   `WebApplication.InvokeAsync`'s `await using var scope = Services.CreateAsyncScope()` really runs
   its `finally` — which is itself nested inside `js/runtime.mjs`'s `scoped()` handle-release
   wrapper, so the .NET scope can never outlive the JS handles its services were built over
   (ADR-0008 §6).

The deliberate-failure case — registering the connection as a singleton and watching the second
request fail — is not a shipped route. A worker that ships a route which corrupts its own isolate
state is a worse sample than one that documents the failure.

## Routes

| Route | Notes |
| --- | --- |
| `GET /api/health` | Injected `ICloudflareEnv` (not the ambient read) and the singleton probe. |
| `GET /api/diag/scope` | The DI proof above. |
| `GET /api/{ado,orm}/notes?take=&skip=` | Query binding with compile-time defaults; the window is clamped, not rejected. |
| `GET /api/{ado,orm}/notes/search?q=&take=` | `q` is **required** — a request without it is a 400 the generated binder produces before the handler is entered. The literal `/search` beats `{id:int}` by precedence. |
| `GET /api/{ado,orm}/notes/{id:int}` | `{id:int}` is enforced by the matcher, so `/notes/abc` is a 404 with no endpoint entered. |
| `POST /api/{ado,orm}/notes` | JSON body bound through `ApiJsonContext`; answers `201` with `Location`. |
| `PUT /api/{ado,orm}/notes/{id:int}` | Rewrites the body and stamps `updated_at`. See the note below on what that stamp can and cannot prove. |
| `DELETE /api/{ado,orm}/notes/{id:int}` | `204` when a row went, `404` when the id was already absent. |

The list endpoints return the list itself rather than an `IResult`: a handler may return a POCO and
the layer serializes it through the app's context. `IReadOnlyList<Note>` is listed in
`ApiJsonContext` in its own right, because metadata is resolved by the **declared** return type.

> **`updated_at` is a stamp, not a proof.** SQLite's `datetime('now')` has one-second resolution, so
> a create and an update issued inside the same second produce an `updated_at` **equal** to
> `created_at` — the observed case in every smoke run, since the script does both in milliseconds.
> That is why `scripts/smoke.mjs` records both stamps but does not assert the stamp moved: a `>`
> there would fail on a fast machine and "pass" only on a slow one. What the smoke asserts instead
> is that the new body survives an **independent follow-up `GET`**, which is the property that
> actually distinguishes a persisted `UPDATE` from one merely echoed back by a `RETURNING` clause.

### Where the two providers genuinely differ

Not everything is symmetric, and pretending otherwise would be the sample lying about an ORM:

* **Search.** The ADO repository uses `instr(lower(body), lower(?))`, so `%` and `_` in the term are
  literal characters. FreeSql translates `Body.Contains(term)` to `LIKE '%term%'`, where they are
  wildcards.
* **Insert.** The ADO repository uses `INSERT … RETURNING`, so the insert and the read-back are one
  statement and one round trip. FreeSql uses `ExecuteIdentityAsync`, which goes through the
  adapter's `SELECT last_insert_rowid()`.
* **`updated_at` on update.** The ADO repository writes `datetime('now')` in the SQL; FreeSql builds
  an `UPDATE` from column assignments, so the value is formatted in C# — with SQLite's own format,
  so the two providers store the same text.

Both write `created_at` the same way: never from C#. The column default is the only writer, so the
database decides what "now" means for both.

## The ADO.NET provider

`backend/Data/**` is copied from [`samples/cloudflare`](../cloudflare/backend/Data), namespace
aside, and ADR-0007 lists it as **sample-only forever** — it is demonstration material, not a
shipped layer, so each sample carries its own copy rather than a shared project.

Two properties of it shape everything above:

* **Commands are async-only.** `ExecuteNonQuery`, `ExecuteScalar` and `ExecuteReader` throw:
  workerd has no thread that could block on a promise. This is why `INoteRepository` is async
  end to end.
* **`BeginTransaction` cannot roll back.** `D1DbTransaction.Rollback` throws, because D1's unit of
  atomicity is `db.batch()`. A repository needing multi-statement atomicity must route it through
  `ID1Database.BatchJson`, not through the ADO transaction API. Nothing in this sample needs it, and
  the sample does not pretend the API works.

## Why linq2db is not the ORM here

It is not blocked, and it is not affordable. Both halves matter, so the evidence is recorded rather
than the conclusion alone.

**It works.** linq2db **6.4.0** executes real queries under NativeAOT-LLVM inside workerd today,
proven on a standalone probe against local D1: a fluent-mapped `notes` entity, one `SELECT` and one
`INSERT`, both returning correct rows. The historical blocker is genuinely fixed upstream —
`LinqToDB.Internal.Reflection.MemberInfoEqualityComparer` read `MemberInfo.MetadataToken`
unguarded in 6.2.1 and 6.3.0, and 6.4.0 gates both call sites on a cached
`try { _ = obj.MetadataToken } catch (InvalidOperationException)` probe that falls back to
`Equals`/`GetHashCode`. NativeAOT throws exactly that exception, so the fallback engages. The same
probe on 6.2.1 still fails with `There is no metadata token available for the given member.`

**It does not fit.** Measured on that probe, same day, same packs: linq2db costs
**+8,307.54 KiB bundle gzip (+8.11 MiB)**, taking a lean probe to 9,374.59 KiB. Three things make
that unavoidable rather than a tuning problem:

1. `<TrimmerRootAssembly Include="linq2db" />` is **required**. With narrower `TrimmerRootType`
   roots the wasm is 9.7 MB smaller and the first `new DataConnection(…)` throws
   `TypeInitializationException` → `NullReferenceException` from a static initializer.
2. A **fake `Microsoft.Data.Sqlite` assembly** (an AssemblyName spoof) is still mandatory:
   `SQLiteProviderAdapter.CreateAdapter` does `TryLoadAssembly("Microsoft.Data.Sqlite")` plus
   `GetType(…, throwOnError: true)` and `SQLiteDataProvider`'s constructor calls it eagerly from its
   `base(…)` call. A consumer that also references the real package is the documented hazard.
3. The package ships **no AOT story**: no trimming doc, no precompiled mapping mode, no
   `[RequiresDynamicCode]` annotations. The 6.4.0 fix is a defensive `try`/`catch`, not a supported
   mode — nothing upstream promises it keeps working.

Against this sample's own measured figures, adding it is arithmetic: this sample without an ORM is
1,499.13 KiB, so linq2db **instead of** FreeSql projects to ≈9,806 KiB — 3.2× the free ceiling and
~430 KiB under the documented paid 10 MiB one — and linq2db **alongside** FreeSql projects to
≈11,840 KiB, which fits no plan at all. *(Projection, not a measurement: the +8.11 MiB delta was
measured on a leaner probe, and it would have to be re-measured here before anyone relied on it.)*

FreeSql remains the ORM demo **on price alone**. The one condition worth re-testing later is narrow
and checkable: whether linq2db's static graph becomes trim-safe enough to drop
`TrimmerRootAssembly`. That single change is what stands between +8.11 MiB and roughly +4.8 MiB —
and even that would not fit beside this sample's other layers.

## Size and startup (measured 2026-08-14)

The number that matters is the **deployable bundle gzip** from `npx wrangler check startup` — wasm
plus ~205 KiB of JS glue — not the wasm file alone. The enforced free-plan ceiling is 3 MiB gzip
(3,072 KiB, API error 10027); the paid 10 MiB figure is documented but not encoded in tooling.

| Variant | wasm raw | wasm gzip -9 | bundle gzip | provenance |
| --- | --- | --- | --- | --- |
| lean control ([`cloudflare-minimal`](../cloudflare-minimal)) | 1,598,716 | 638,448 | **748.94 KiB** | recorded earlier the same day; not re-measured here |
| control + `Bootsharp.Cloudflare.AspNetCore` | — | — | **1,085.0 KiB** (+336.1) | derived: 766,915 + the same-day +344,125 layer delta |
| **this sample without the ORM** (DI + ADO + D1, 8 routes) | 3,553,480 | 1,389,177 | **1,499.13 KiB** (+414.1) | measured twice, independently, on clean rebuilds |
| **this sample as shipped** (+ FreeSql, 14 routes) | 9,642,665 | 3,400,865 | **3,532.38 KiB** (+2,033.3) | measured twice, independently, on clean rebuilds |

Read across, that is the first attributable split of this story's cost — every previously recorded
FreeSql figure was measured on the pre-ADR-0008 `AspNetShim`:

* **The DI + ADO half costs ~414 KiB** over the Minimal API layer, and lands at **1,499 KiB — well
  inside the free plan**, with ~1,573 KiB to spare. That half is the part most apps want.
* **FreeSql costs ~2,033 KiB (1.99 MiB)** — 58% of the shipped bundle, and the entire reason this
  sample is **460.38 KiB over the free ceiling** and needs a paid plan.

Two caveats on that table, so it is not read as more than it is. The bottom two rows are the only
ones measured here; the second row is **derived**, not measured — and the same-day source it is
derived from carries a second figure for the same layer (1,147,249 B, from a differently-shaped
baseline whose four hand-routed endpoints were rewritten as `Map*`). So treat the +414 KiB
attribution as approximate at its top end and the +2,033 KiB FreeSql delta — a difference between
two builds of *this* project, one day, one toolchain — as the solid number.

The two measured rows have since been **reproduced independently**: a clean rebuild from a purged
`dist/`, `obj/` and `bin/` returned a byte-identical 9,642,665 B wasm and the same 3,532.38 KiB
bundle, and a freshly reconstructed ORM-free probe returned a byte-identical 3,553,480 B wasm and
the same 1,499.13 KiB bundle. So the +2,033.25 KiB FreeSql delta is a repeatable measurement rather
than a single observation.

Against the lean control the split reads: this sample adds **2,783.4 KiB** in total, of which
FreeSql alone is **2,033.25 KiB — 73% of everything the sample adds**, and 58% of the shipped
bundle. The whole DI, ADO.NET, D1 and Minimal API story together costs the remaining ~750 KiB.
*(The lean control is the one figure here taken on trust rather than re-measured, and the sources
for it disagree slightly — 748.94 KiB in the row above against 768,768 B / 750.75 KiB elsewhere, a
0.2% spread. Either way the conclusion below is unchanged, but the control is what a merge gate
should re-measure.)*

Startup CPU is a non-issue either way, but the figure is noisy and should be read as such: the local
profile sampled **10.6 ms active** without the ORM and **12.8–17.1 ms** across runs for the shipped
sample, against a 400 ms budget. `wrangler check startup` takes ~17 samples over a ~30 ms window on
one local CPU, so run-to-run spread of several milliseconds is the instrument, not the worker. Boot
is lazy — the isolate startup phase evaluates only the JS shim, and .NET instantiation happens
inside the first request, which is also where `app.Run()` builds the endpoint table.

## Smoke test

```bash
npm run publish:cs && npm run smoke
```

`scripts/smoke.mjs` boots this worker under real workerd on port **8798** and runs ten checks: the
full CRUD surface through each provider, the search endpoint through each, one note written by the
ORM and read back by the hand-written SQL (one table, two providers), the router's and the binder's
negatives — and the two that exist only for the DI story, `diScope` and `handleLifetime`, described
above.

Results land in `smoke-results.json`, which records **the verbatim exchanges** — method, path,
status, `Location`/`Allow` where present, and the response body as it came back — beside each
check's verdict, plus the timestamp of the run. The booleans are what the script concluded; the
exchanges are what the worker actually said, so a reader can check one against the other without
rerunning anything. A `diScope` entry, for instance, carries both scope reports in full, which is
the whole proof in six lines of JSON.

It is not a substitute for the C# suites, which assert the same behaviours far more finely and in
milliseconds. What it covers that they structurally cannot is the crossing itself: that a D1
statement reached workerd, that the rows came back through the interop boundary, and that the
handles all of it was built over were still alive when they were used.
