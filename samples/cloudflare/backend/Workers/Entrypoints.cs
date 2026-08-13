namespace Cloudflare.Workers;

/// <summary>
/// C# counterpart of JS <c>class extends DurableObject</c> from <c>cloudflare:workers</c>.
/// workerd still constructs the JS subclass; this type holds the guest instance.
/// </summary>
public abstract class DurableObject
{
    protected DurableObject(IDurableObjectState ctx, ICloudflareEnv env)
    {
        Ctx = ctx;
        Env = env;
    }

    protected IDurableObjectState Ctx { get; }
    protected ICloudflareEnv Env { get; }
}

/// <summary>
/// C# counterpart of JS <c>class extends WorkflowEntrypoint</c>.
/// Implement <see cref="Run"/>; the generated shim calls it from JS <c>run(event, step)</c>.
/// </summary>
public abstract class WorkflowEntrypoint
{
    protected WorkflowEntrypoint(IExecutionContext ctx, ICloudflareEnv env)
    {
        Ctx = ctx;
        Env = env;
    }

    protected IExecutionContext Ctx { get; }
    protected ICloudflareEnv Env { get; }

    public abstract Task Run(WorkflowEvent evt, IWorkflowStep step);
}

/// <summary>
/// C# counterpart of JS <c>class extends WorkerEntrypoint</c> from <c>cloudflare:workers</c>.
/// Implement <see cref="Fetch"/>; the source generator emits the JS default export that
/// boots WASM and JSImports <c>request</c> / <c>env</c> into this type.
/// </summary>
public abstract class WorkerEntrypoint
{
    public abstract Task<Cloudflare.Backend.HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env);

    public virtual Task Queue(string messagesJson, ICloudflareEnv env) => Task.CompletedTask;

    /// <summary>
    /// Cron trigger (<c>triggers.crons</c>). The generator projects handlers from the members a
    /// subclass declares, so overriding this is what emits a JS <c>scheduled</c> handler.
    /// </summary>
    public virtual Task Scheduled(IScheduledController controller, ICloudflareEnv env) => Task.CompletedTask;
}
