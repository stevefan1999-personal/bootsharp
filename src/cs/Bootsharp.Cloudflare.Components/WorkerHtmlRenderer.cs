using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bootsharp.Cloudflare.Components;

/// <summary>
/// Renders <c>.razor</c> components to HTML inside a workerd isolate.
/// </summary>
/// <remarks>
/// <para>
/// A three-line subclass, and the three lines are the whole design. It derives from
/// <c>StaticHtmlRenderer</c> rather than going through <c>HtmlRenderer</c> because that facade is
/// <c>sealed</c> and constructs its own renderer, so it cannot carry the dispatcher below — and the
/// dispatcher is the only part of Blazor's rendering infrastructure that is not obviously safe here.
/// </para>
/// <para>
/// Everything else is reused as-is and was proven to work: the whole of
/// <c>Microsoft.AspNetCore.Components</c> contains no <c>Reflection.Emit</c>, no
/// <c>DynamicMethod</c>, no <c>MetadataToken</c> and nothing marked
/// <c>[RequiresDynamicCode]</c>; parameter and <c>[Inject]</c> assignment take the
/// <c>IsDynamicCodeSupported == false</c> branch and use <c>PropertyInfo.SetValue</c>; the one
/// <c>Expression.Compile</c> in the surface is behind the same feature switch and ILC removes it.
/// </para>
/// </remarks>
public sealed class WorkerHtmlRenderer : StaticHtmlRenderer
{
    /// <summary>Runs every work item inline, on the thread that queued it.</summary>
    /// <remarks>
    /// The base's default is <c>Dispatcher.CreateDefault()</c>, whose <c>PostAsync</c> awaits with
    /// <c>ConfigureAwaitOptions.ForceYielding</c> — a thread-pool hop, which on single-threaded wasm
    /// becomes the runtime's timer queue and so can outlive the <c>fetch</c> event that started it.
    /// Blazor WebAssembly answers the same problem the same way when it is single-threaded, swapping
    /// in an internal <c>NullDispatcher</c>; that type is <c>internal sealed</c> in an assembly this
    /// package does not reference, so the thirty lines are rewritten rather than reused. The result
    /// is that a render cannot post work outside the event it was started in — which turns
    /// "Components probably works in a worker" into something provable.
    /// </remarks>
    public override Dispatcher Dispatcher { get; } = new InlineDispatcher();

    public WorkerHtmlRenderer (IServiceProvider services, ILoggerFactory? loggerFactory = null)
        : base(services, loggerFactory ?? NullLoggerFactory.Instance) { }

    private sealed class InlineDispatcher : Dispatcher
    {
        public override bool CheckAccess () => true;

        public override Task InvokeAsync (Action workItem)
        {
            workItem();
            return Task.CompletedTask;
        }

        public override Task InvokeAsync (Func<Task> workItem) => workItem();

        public override Task<TResult> InvokeAsync<TResult> (Func<TResult> workItem) =>
            Task.FromResult(workItem());

        public override Task<TResult> InvokeAsync<TResult> (Func<Task<TResult>> workItem) => workItem();
    }
}
