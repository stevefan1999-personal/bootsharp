namespace Bootsharp.Cloudflare;

/// <summary>
/// JS <c>Workflow</c> binding (<c>env.WORKFLOW</c>). <c>createBatch</c> omitted
/// (array of instance handles).
/// </summary>
/// <remarks>An env binding: workerd hands out the same object for the lifetime of the isolate,
/// so the handle is exempt from per-invocation release.</remarks>
[JSHandle(Scope = HandleScope.Isolate)]
public interface IWorkflow
{
    Task<IWorkflowInstance> Create(WorkflowInstanceCreateOptions? options);
    Task<IWorkflowInstance> Get(string id);
    Task<WorkflowBatchDeleteResult> DeleteBatch(string[] instanceIds);
}

/// <summary>JS <c>WorkflowInstanceCreateOptions</c>. <c>params</c> is JSON text (TS <c>unknown</c>).</summary>
public sealed record WorkflowInstanceCreateOptions
{
    public string? Id { get; init; }
    public string? Params { get; init; }
    public string? SuccessRetention { get; init; }
    public string? ErrorRetention { get; init; }
}

/// <summary>JS <c>WorkflowBatchDeleteResult</c>.</summary>
public sealed record WorkflowBatchDeleteResult(
    WorkflowBatchDeleted[] Deleted,
    WorkflowBatchDeleteError[] Errors);

/// <summary>One successfully deleted instance id.</summary>
public sealed record WorkflowBatchDeleted(string Id);

/// <summary>Per-id error from <c>deleteBatch</c>.</summary>
public sealed record WorkflowBatchDeleteError(string Id, int Code, string Message);

/// <summary>JS <c>WorkflowInstance</c>.</summary>
public interface IWorkflowInstance
{
    string Id { get; }
    Task Pause();
    Task Resume();
    Task Terminate(WorkflowInstanceTerminateOptions? options);
    Task Restart(WorkflowInstanceRestartOptions? options);
    /// <summary>JS <c>delete</c> → <c>$delete</c>.</summary>
    Task Delete();
    /// <summary>JSON <c>InstanceStatus</c>.</summary>
    Task<string> Status();
    Task SendEvent(string type, string payloadJson);
}

/// <summary>JS <c>WorkflowInstanceTerminateOptions</c>.</summary>
public sealed record WorkflowInstanceTerminateOptions
{
    public bool? Rollback { get; init; }
}

/// <summary>JS <c>WorkflowInstanceRestartOptions</c> (<c>from</c> flattened).</summary>
public sealed record WorkflowInstanceRestartOptions
{
    public string? FromName { get; init; }
    public int? FromCount { get; init; }
    public string? FromType { get; init; }
}

/// <summary>
/// JS <c>WorkflowStep</c>. TS <c>do</c> overloads collapse to <see cref="Do"/> /
/// <see cref="DoWithConfig"/>. C# <c>Do</c> is JS <c>$do</c>.
/// </summary>
public interface IWorkflowStep
{
    Task<string> Do(string name, Func<Task<string>> callback);
    Task<string> DoWithConfig(string name, WorkflowStepConfig config, Func<Task<string>> callback);
    Task Sleep(string name, string duration);
    Task SleepUntil(string name, double timestampMs);
    /// <summary>JSON <c>WorkflowStepEvent</c>.</summary>
    Task<string> WaitForEvent(string name, string type);
}

/// <summary>Subset of JS <c>WorkflowStepConfig</c> (static delay only, no delay function).</summary>
public sealed record WorkflowStepConfig
{
    public int? RetryLimit { get; init; }
    public string? RetryDelay { get; init; }
    public string? Timeout { get; init; }
}

/// <summary>JS <c>WorkflowEvent&lt;T&gt;</c> with <c>payload</c> as JSON text.</summary>
public sealed record WorkflowEvent(
    string Payload,
    string Timestamp = "",
    string InstanceId = "",
    string WorkflowName = "");
