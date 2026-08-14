namespace Llvm.Exercise;

/// <summary>The exercise worker's wrangler bindings, expressed in C#.</summary>
[WorkerEnv]
public interface IExerciseEnv
{
    /// <summary>An isolate-lived handle: memoized by the emitted env wrapper, so the same
    /// JavaScript object — and hence the same C# proxy — must be seen by every invocation.</summary>
    IKvNamespace KV { get; }
    IProbeNamespace PROBE { get; }
    string ENVIRONMENT { get; }
}

/// <summary>
/// wrangler <c>PROBE</c> binding: TS <c>DurableObjectNamespace&lt;Probe&gt;</c>. Isolate-lived,
/// like every env binding — the emitted <c>wrapEnv</c> builds the adapter once per isolate.
/// </summary>
[JSHandle(Scope = HandleScope.Isolate)]
public interface IProbeNamespace
{
    IProbeStub GetByName (string name);
}

/// <summary>
/// A Durable Object stub. Deliberately NOT isolate-scoped: workerd request-scopes stubs, so the
/// handle must be released when the invocation that imported it ends. The lane
/// asserts exactly that, by holding one past its invocation and expecting the call to fail.
/// </summary>
public interface IProbeStub
{
    /// <summary>
    /// The shape the <c>RpcInt</c> box existed for: a primitive behind a workerd
    /// <c>JsRpcPromise</c>. It only marshals because the generated import awaits it.
    /// </summary>
    Task<int> Increment ();
}
