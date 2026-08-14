global using Bootsharp;
global using Bootsharp.Cloudflare;
// The Minimal API surface. Global rather than per-file because every route file and the service
// behind them is written against it — the same three usings a `dotnet new web` Program.cs gets
// implicitly from the Web SDK, which a browser-wasm project does not use.
global using Bootsharp.Cloudflare.AspNetCore;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;

// The worker context is generic over the app's env (the library cannot name it), and every call
// site in this app means the one closed over ICloudflareEnv. The alias keeps them reading as
// WorkerContext.Env rather than repeating the closure.
global using WorkerContext = Bootsharp.Cloudflare.WorkerContext<Cloudflare.Backend.ICloudflareEnv>;
