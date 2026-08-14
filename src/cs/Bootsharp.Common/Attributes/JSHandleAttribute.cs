namespace Bootsharp;

/// <summary>
/// Lifetime of the JavaScript objects carried by a handle type.
/// </summary>
public enum HandleScope
{
    /// <summary>
    /// The handle is released when the invocation that imported it ends (default).
    /// </summary>
    Invocation,
    /// <summary>
    /// The handle is never tracked by an invocation scope; it lives as long as the host isolate.
    /// </summary>
    Isolate
}

/// <summary>
/// Declares the annotated interface an opaque JavaScript handle: its instances cross the interop
/// boundary by reference with no adapter and no serializer, the generated import proxy gets a
/// deterministic <see cref="IDisposable.Dispose"/> and the emitted TypeScript declaration is
/// replaced with <see cref="Decl"/>, when specified.
/// </summary>
/// <remarks>
/// Extend <see cref="IDisposable"/> on the annotated interface to make the generated deterministic
/// release reachable from the consumer code, which holds the handle at the interface type. The
/// interface must not declare a Dispose member of its own, as it would collide with the generated one.
/// </remarks>
/// <param name="Decl">
/// TypeScript type the handle is declared as, eg 'ReadableStream', which is emitted as
/// 'export type $name = ReadableStream;'. When the value starts with 'export ' it is instead used
/// verbatim as the whole declaration. Occurrences of '$name' are replaced with the name of the
/// handle type and '$full' — with its fully-qualified name.
/// </param>
/// <example>
/// Declare a member-less handle over the host's ReadableStream:
/// <code>
/// [JSHandle("ReadableStream")]
/// public interface IReadableStream : IDisposable;
/// </code>
/// Declare a handle that outlives the invocation importing it:
/// <code>
/// [JSHandle(Scope = HandleScope.Isolate)]
/// public interface IExecutionContext { void WaitUntil (Task task); }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class JSHandleAttribute (string? Decl = null) : Attribute
{
    /// <inheritdoc cref="JSHandleAttribute(string)"/>
    public string? Decl { get; } = Decl;
    /// <summary>
    /// Lifetime of the JavaScript objects carried by the handle type; <see cref="HandleScope.Invocation"/> by default.
    /// </summary>
    public HandleScope Scope { get; init; } = HandleScope.Invocation;
}
