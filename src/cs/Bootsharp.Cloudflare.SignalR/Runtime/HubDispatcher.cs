using Microsoft.AspNetCore.SignalR;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// The contract the <b>generated</b> hub dispatch implements — mechanism, second
/// consumer. Upstream's <c>DefaultHubDispatcher</c> discovers methods with
/// <c>hubType.GetMethods()</c> plus <c>GetInterfaceMap</c> and invokes them through
/// <c>MethodInfo.Invoke</c> even on its "trim/AOT-compatible" path
/// (<c>ObjectMethodExecutor.cs:76</c>). None of that survives NativeAOT-LLVM at a defensible size,
/// and no upstream generator exists (<c>grep -rl IIncrementalGenerator src/SignalR/server</c> is
/// empty), so the table below is emitted instead.
/// </summary>
/// <remarks>
/// Everything a generated implementation returns is a compile-time constant of the app: the slot
/// table, the parameter-type arrays that feed <see cref="IInvocationBinder"/>, and a switch of
/// direct, unboxed calls. There is no <c>MethodInfo</c>, no <c>DynamicallyAccessedMembers</c> root
/// and no reflection-invoke stub anywhere on the path.
/// </remarks>
public abstract class HubDispatcher<THub> where THub : Hub
{
    /// <summary>
    /// Slot of a hub method by its wire name, or -1. Case-insensitive, matching upstream's
    /// <c>new Dictionary&lt;string, HubMethodDescriptor&gt;(StringComparer.OrdinalIgnoreCase)</c>
    /// (<c>DefaultHubDispatcher.cs:25</c>), and honouring <c>[HubMethodName]</c>.
    /// </summary>
    public abstract int Slot (string methodName);

    /// <summary>
    /// Parameter types of the numbered slot, feeding <see cref="IInvocationBinder.GetParameterTypes"/>.
    /// A static array per method: the type feed the reused JSON parser needs, with no reflection.
    /// </summary>
    public abstract IReadOnlyList<Type> ParameterTypes (int slot);

    /// <summary>
    /// Invokes the numbered slot. The generated body is a <c>switch</c> of direct calls with the
    /// arguments cast to their declared types.
    /// </summary>
    /// <returns>The method's result, or null for a void/Task-returning method.</returns>
    public abstract ValueTask<object?> Invoke (int slot, THub hub, object?[] args);

    /// <summary>Constructs the hub. Generated as <c>new THub()</c>; overridable for a hub with dependencies.</summary>
    public abstract THub Create ();

    /// <summary>
    /// Wire names of every dispatchable method, for diagnostics and for the "no such method" error
    /// upstream produces from its dictionary miss.
    /// </summary>
    public abstract IReadOnlyList<string> MethodNames { get; }
}
