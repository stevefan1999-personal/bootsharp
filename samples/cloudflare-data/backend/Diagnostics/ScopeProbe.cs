namespace Cloudflare.Data.Diagnostics;

/// <summary>Identifies one DI scope — that is, one worker event.</summary>
public interface IScopeProbe
{
    /// <summary>Unique per resolved instance, so two scopes cannot report the same value.</summary>
    Guid ScopeId { get; }

    /// <summary>
    /// How many scopes this isolate had opened when this one was created, 1-based.
    /// </summary>
    /// <remarks>
    /// Carried beside <see cref="ScopeId"/> deliberately: a counter is legible in a diff of two
    /// responses in a way a pair of guids is not, and if <c>Guid.NewGuid</c> ever degenerated under
    /// NativeAOT-LLVM this is the field that would show it.
    /// </remarks>
    int Sequence { get; }
}

/// <summary>Isolate-wide facts, and the counter that makes scope disposal observable.</summary>
public interface IIsolateProbe
{
    /// <summary>Constant for the life of the isolate; changes only when workerd starts a new one.</summary>
    Guid IsolateId { get; }

    /// <summary>Scopes opened so far, including the one currently running.</summary>
    int ScopesOpened { get; }

    /// <summary>Scopes whose <c>DisposeAsync</c> has completed.</summary>
    int ScopesDisposed { get; }

    /// <summary>Called by <see cref="ScopeProbe"/>; not by application code.</summary>
    int OpenScope ();

    /// <summary>Called by <see cref="ScopeProbe"/>; not by application code.</summary>
    void CloseScope ();
}

/// <summary>
/// The singleton half of the scope proof.
/// </summary>
/// <remarks>
/// Registered as a singleton and holding no interop handle, which is what makes that legal: the
/// only reason a service in this worker must be scoped is that it reaches, directly or indirectly,
/// a JS handle whose id is released when the event ends. This one reaches nothing.
/// </remarks>
internal sealed class IsolateProbe : IIsolateProbe
{
    private int opened;
    private int disposed;

    public Guid IsolateId { get; } = Guid.NewGuid();
    public int ScopesOpened => Volatile.Read(ref opened);
    public int ScopesDisposed => Volatile.Read(ref disposed);

    public int OpenScope () => Interlocked.Increment(ref opened);
    public void CloseScope () => Interlocked.Increment(ref disposed);
}

/// <summary>
/// The scoped half of the scope proof.
/// </summary>
/// <remarks>
/// <see cref="IAsyncDisposable"/> on purpose. <c>WebApplication.InvokeAsync</c> opens the event's
/// scope with <c>await using var scope = Services.CreateAsyncScope()</c>, so the container calls
/// <see cref="DisposeAsync"/> here when the event ends — and the counter it bumps is the only way a
/// caller can tell "the scope was disposed" from "the scope was merely abandoned". A response that
/// reports N-1 disposals on request N is that <c>finally</c> having run every previous time.
/// </remarks>
internal sealed class ScopeProbe (IIsolateProbe isolate) : IScopeProbe, IAsyncDisposable
{
    public Guid ScopeId { get; } = Guid.NewGuid();
    public int Sequence { get; } = isolate.OpenScope();

    public ValueTask DisposeAsync ()
    {
        isolate.CloseScope();
        return ValueTask.CompletedTask;
    }
}
