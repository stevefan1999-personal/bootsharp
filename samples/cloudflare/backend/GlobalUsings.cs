global using Bootsharp.Cloudflare;

// The worker context is generic over the app's env (the library cannot name it), and every call
// site in this app means the one closed over ICloudflareEnv. The alias keeps them reading as
// WorkerContext.Env rather than repeating the closure.
global using WorkerContext = Bootsharp.Cloudflare.WorkerContext<Cloudflare.Backend.ICloudflareEnv>;
