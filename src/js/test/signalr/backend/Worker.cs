namespace SignalR.Harness;

/// <summary>
/// Present so the publish task emits a worker module at all. The harness routes in JavaScript: a
/// WebSocket upgrade answers with a 101 carrying a live socket object, which is not a shape the C#
/// response snapshot can express.
/// </summary>
public sealed class Worker : WorkerEntrypoint<IHarnessEnv, HarnessResponse>, IWorker
{
    public override Task<HarnessResponse> Fetch (IJsRequest request, IHarnessEnv env) =>
        Task.FromResult(new HarnessResponse(404, """{"content-type":"application/json"}""",
            """{"error":"the harness routes in JavaScript"}"""));
}
