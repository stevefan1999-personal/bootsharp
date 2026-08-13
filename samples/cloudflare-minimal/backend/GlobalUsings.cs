global using Bootsharp.Cloudflare;

// The worker context is generic over the app's env — the library cannot name it — and every call
// site here means the one closed over IWorkerEnv. The alias keeps them reading as
// WorkerContext.Env rather than repeating the closure.
global using WorkerContext = Bootsharp.Cloudflare.WorkerContext<Cloudflare.Minimal.IWorkerEnv>;
