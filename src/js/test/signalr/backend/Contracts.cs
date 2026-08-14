namespace SignalR.Harness;

/// <summary>Exported WASM surface: the worker entrypoint the generated module calls into.</summary>
public interface IWorker
{
    Task<HarnessResponse> Fetch(IJsRequest request, IHarnessEnv env);
}

/// <summary>The three fields <c>js/runtime.mjs</c>'s <c>toResponse</c> reads.</summary>
public sealed record HarnessResponse(int Status, string HeadersJson, string Body);

/// <summary>Guest-side actor registry, as every Bootsharp.Cloudflare app declares it.</summary>
public interface IActorRuntime
{
    int ConstructDurableObject(string className, IDurableObjectState ctx, IHarnessEnv env);
    int ConstructWorkflow(string className, IExecutionContext ctx, IHarnessEnv env);
    Task<string> CallDurableObject(int id, string method, string argsJson);
    Task<string> RunWorkflow(int id, string payloadJson, IWorkflowStep step);
}

/// <summary>The app's half of the actor dispatch partial; the generated half is the switches.</summary>
public sealed partial class ActorRuntime : ActorRuntimeBase<IHarnessEnv>, IActorRuntime;
