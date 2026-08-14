# Bootsharp on Cloudflare Workers — `.cshtml`, FreeSql, D1

A Worker whose UI is **`.cshtml` page components**, whose data layer is **FreeSql over
Cloudflare D1**, and whose graph is **real constructor injection** with isolate-correct
lifetimes.

`Bootsharp.Cloudflare.Razor` compiles each page at build time into `HtmlWriter` calls.
Nothing of the Razor compiler or MVC reaches the isolate (ADR-0011 §3).

## Why not `.razor` components

ADR-0011 §2: `Bootsharp.Cloudflare.Components` links and passes CoreCLR tests, but
**does not render under NativeAOT-LLVM in workerd**. `RenderTreeFrame` is an explicit-layout
union of reference fields; wasm32 ILC reads them back as null. Static markup becomes an
empty 200; a parameterized component throws. This sample therefore composes **`.cshtml`
components** (`Greeting.cshtml`, `NoteCard.cshtml`) — the shipping Razor story — instead
of shipping a route that cannot paint.

## What it shows

- Scoped DI: `IWorkerEnv`, `IFreeSql`, `INoteRepository`, `PagesService` all live one
  event. The worker entrypoint is the only singleton.
- FreeSql over D1 through `D1DbConnection` (no native SQLite in workerd).
- `Pages/Home.cshtml` calling `Greeting.Render` and `NoteCard.Render` as method-shaped
  components.
- Context-aware encoding (`javascript:` → `about:invalid`).

## Run

```
src/cs/.scripts/pack.sh
cd samples/cloudflare-razor
npm install
npm run migrate:local
npm run dev
```

Live: https://bootsharp-cloudflare-razor.stevefan1999.workers.dev

Deployed bundle gzip is **~3.4 MiB** (FreeSql). That is over the 3 MiB free-plan ceiling;
this account accepted the upload. Drop the ORM if you need to stay on free.

Remote schema (already applied for the deployed worker):

```
npm run migrate:remote
npm run deploy
```

## Routes

| Path | Result |
| --- | --- |
| `/` | home page, notes from D1 |
| `/?name=Ada` | greeting component |
| `POST /notes` | create a note (HTML form) |
| `POST /notes/{id}/delete` | delete |
| `/api/notes` | JSON list |
| `POST /api/notes` | JSON create |
| `/api/health` | JSON |
