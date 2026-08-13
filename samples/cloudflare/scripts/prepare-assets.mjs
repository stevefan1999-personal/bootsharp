#!/usr/bin/env node
import { mkdir, writeFile, cp } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const www = path.join(root, "dist/wwwroot");
await mkdir(www, { recursive: true });
await mkdir(path.join(www, "app"), { recursive: true });

const blazorOut = path.join(root, "frontend/bin/Release/net10.0/wwwroot");
if (existsSync(blazorOut)) {
  await cp(blazorOut, www, { recursive: true });
}

const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <title>Bootsharp Blazor on Cloudflare Pages</title>
  <style>
    :root { --bg:#0c0a12; --ink:#f4f0ff; --muted:#a89bb8; --accent:#8b5cf6; --line:#2c2438; --panel:#161221; }
    html,body { margin:0; background:var(--bg); color:var(--ink); font:16px/1.5 ui-sans-serif,system-ui,sans-serif; }
    main { max-width: 720px; margin: 0 auto; padding: 2.5rem 1.25rem; }
    a { color:#22d3ee; }
    .card { background:var(--panel); border:1px solid var(--line); border-radius:16px; padding:1.2rem; margin:1rem 0; }
    button,input { font:inherit; border-radius:10px; border:1px solid var(--line); background:#0a0810; color:var(--ink); padding:.5rem .7rem; }
    button { background:var(--accent); border-color:transparent; cursor:pointer; font-weight:650; }
    pre { background:#0a0810; padding:.8rem; border-radius:10px; overflow:auto; }
    h1 { letter-spacing:-.03em; }
  </style>
</head>
<body>
  <main>
    <p><a href="/">← C# SSR home</a></p>
    <h1>Blazor WASM frontend</h1>
    <p style="color:var(--muted)">This static app is the Cloudflare Pages / Workers Assets frontend. It talks to the C# NativeAOT-LLVM Worker over <code>/api/*</code>. When the Blazor publish is present, its <code>_framework</code> payload is served from the same origin.</p>
    <div class="card">
      <h2>Health</h2>
      <pre id="health">loading…</pre>
      <button id="reload">Reload APIs</button>
    </div>
    <div class="card">
      <h2>KV write from the browser</h2>
      <input id="kv" placeholder="value" value="from-blazor"/>
      <button id="put">POST /api/kv</button>
    </div>
  </main>
  <script>
    async function load() {
      const health = await fetch("/api/health").then(r => r.text());
      document.getElementById("health").textContent = health;
    }
    document.getElementById("reload").onclick = load;
    document.getElementById("put").onclick = async () => {
      const value = document.getElementById("kv").value;
      await fetch("/api/kv", { method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" }, body: "key=demo&value=" + encodeURIComponent(value) });
      await load();
    };
    load();
  </script>
</body>
</html>`;

await writeFile(path.join(www, "app/index.html"), html);
await writeFile(path.join(www, "index.html"), html);
console.log("assets ready", www);
