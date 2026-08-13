namespace Cloudflare.Backend;

/// <summary>
/// User workflow. wrangler <c>class_name</c> is this type name.
/// No JavaScript class to write — publish emits <c>export class DemoWorkflow extends WorkflowEntrypoint</c>.
/// </summary>
public sealed class DemoWorkflow : WorkflowEntrypoint
{
    public DemoWorkflow(IExecutionContext ctx, ICloudflareEnv env) : base(ctx, env) { }

    public override async Task Run(WorkflowEvent evt, IWorkflowStep step)
    {
        var started = await step.Do("start", () => Task.FromResult(evt.Payload));
        await step.Sleep("pause", "1 second");
        // Retries/timeout: use step.DoWithConfig("finish", new WorkflowStepConfig { ... }, ...).
        await step.Do("finish", () => Task.FromResult(started + ":done"));
    }
}
