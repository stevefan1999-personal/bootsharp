namespace Bootsharp.Cloudflare;

/// <summary>
/// The <c>env</c> handle every entrypoint base is parameterised over. An app declares its own
/// interface listing exactly the bindings its wrangler configuration provides (: the
/// binding surface is app config, the projections of those bindings are the library's), so the
/// bases cannot name a concrete env type — they take it as <c>TEnv</c> instead.
/// </summary>
/// <remarks>
/// Constraining to <c>class</c> is what makes the env storable in an <c>AsyncLocal</c> slot with
/// "unbound" distinguishable from "bound to default": a struct env would have no null state.
/// </remarks>
public abstract class DurableObject<TEnv> where TEnv : class
{
    protected DurableObject (IDurableObjectState ctx, TEnv env)
    {
        Ctx = ctx;
        Env = env;
    }

    protected IDurableObjectState Ctx { get; }
    protected TEnv Env { get; }
}

/// <summary>
/// Env-agnostic half of <see cref="WorkflowEntrypoint{TEnv}"/>: the generated actor dispatch runs
/// a workflow without knowing which env the app parameterised it with, so <c>Run</c> is reachable
/// through this interface rather than through a constructed generic type.
/// </summary>
public interface IWorkflowEntrypoint
{
    Task Run (WorkflowEvent evt, IWorkflowStep step);
}

/// <summary>
/// C# counterpart of JS <c>class extends WorkflowEntrypoint</c>.
/// Implement <see cref="Run"/>; the generated shim calls it from JS <c>run(event, step)</c>.
/// </summary>
public abstract class WorkflowEntrypoint<TEnv> : IWorkflowEntrypoint where TEnv : class
{
    protected WorkflowEntrypoint (IExecutionContext ctx, TEnv env)
    {
        Ctx = ctx;
        Env = env;
    }

    protected IExecutionContext Ctx { get; }
    protected TEnv Env { get; }

    public abstract Task Run (WorkflowEvent evt, IWorkflowStep step);
}

/// <summary>
/// C# counterpart of JS <c>class extends WorkerEntrypoint</c> from <c>cloudflare:workers</c>.
/// Implement <see cref="Fetch"/>; the source generator emits the JS default export that
/// boots WASM and JSImports <c>request</c> / <c>env</c> into this type.
/// </summary>
/// <remarks>
/// <c>TResponse</c> is the app's response snapshot: with no browser-wasm runtime pack for
/// Microsoft.AspNetCore.App, the HTTP contract cannot live in this package until the
/// AspNetCore layer vendors one, so the base takes whatever record the app returns.
/// </remarks>
public abstract class WorkerEntrypoint<TEnv, TResponse> where TEnv : class
{
    public abstract Task<TResponse> Fetch (IJsRequest request, TEnv env);

    public virtual Task Queue (string messagesJson, TEnv env) => Task.CompletedTask;

    /// <summary>
    /// Cron trigger (<c>triggers.crons</c>). The generator projects handlers from the members a
    /// subclass declares, so overriding this is what emits a JS <c>scheduled</c> handler.
    /// </summary>
    public virtual Task Scheduled (IScheduledController controller, TEnv env) => Task.CompletedTask;
}
