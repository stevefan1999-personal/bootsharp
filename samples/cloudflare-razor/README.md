# Bootsharp on Cloudflare Workers — `.cshtml` sample

A Worker whose home page is a **`.cshtml` file**. `Bootsharp.Cloudflare.Razor` compiles the page
at build time into calls on the shipped `HtmlWriter`. Nothing of the Razor compiler or MVC
reaches the isolate (ADR-0011 §3).

Sibling of [`samples/cloudflare-minimal`](../cloudflare-minimal) (no SSR) and
[`samples/cloudflare`](../cloudflare) (the full kitchen sink, which also has a `.cshtml` page).

## What it shows

- `Pages/Home.cshtml` with `@model`, `@if`, and context-aware encoding
- `TypedResults.Html(html => Home.Render(html, model))` as the handler
- Query binding (`?name=`, `?probe=`) through Minimal API
- A `javascript:` probe that the URL sink turns into `about:invalid`

## Run

```
src/cs/.scripts/pack.sh          # once: local Bootsharp packages, including Razor
cd samples/cloudflare-razor
npm install
npm run dev
```

`npm run deploy` publishes NativeAOT-LLVM then `wrangler deploy`.

Live: https://bootsharp-cloudflare-razor.stevefan1999.workers.dev

## Routes

| Path | Result |
| --- | --- |
| `/` | compiled `.cshtml` |
| `/?name=Ada` | same page, greeting bound from the query |
| `/?probe=javascript:alert(1)` | href becomes `about:invalid` |
| `/api/health` | JSON |
