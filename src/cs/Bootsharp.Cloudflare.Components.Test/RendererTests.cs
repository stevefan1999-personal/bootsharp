using Bootsharp.Cloudflare.Components.Tests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bootsharp.Cloudflare.Components.Tests;

/// <summary>What <c>Injecting.razor</c> asks for, to exercise the <c>[Inject]</c> path.</summary>
public interface IGreeter
{
    string Greet ();
}

internal sealed class Greeter : IGreeter
{
    public string Greet () => "from di";
}

/// <summary>
/// The opt-in Razor Components tier, rendered through the same renderer a worker uses.
/// </summary>
/// <remarks>
/// proved Components static SSR compiles and runs under NativeAOT and measured its
/// price; what it did not do is run a component whose <c>OnInitializedAsync</c> actually suspends,
/// which is the one thing the inline dispatcher exists for (its open question 3). These cases pin
/// that behaviour on the managed side; the sample's <c>/components</c> route pins it inside workerd.
/// </remarks>
public class RendererTests
{
    private static IServiceProvider Services (Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static Task<string> Render<TComponent> (IDictionary<string, object?>? parameters = null,
        IServiceProvider? services = null) where TComponent : IComponent =>
        Rendered<TComponent>(parameters, services);

    private static async Task<string> Rendered<TComponent> (IDictionary<string, object?>? parameters,
        IServiceProvider? services) where TComponent : IComponent =>
        (await ComponentRenderer.RenderAsync<TComponent>(services ?? Services(), parameters)).Value;

    [Fact]
    public async Task RendersAComponentWithItsParameters ()
    {
        var html = await Render<Greeting>(new Dictionary<string, object?> {
            ["Name"] = "Bootsharp", ["Items"] = new[] { "a", "b" }
        });
        Assert.Equal("<h1 class=\"greeting\">Hello, Bootsharp!</h1>\n<ul><li>a</li><li>b</li></ul>", html);
    }

    /// <summary>Components' own encoder, not this package's — but the property has to hold anyway.</summary>
    [Fact]
    public async Task EncodesParameterValues ()
    {
        var html = await Render<Greeting>(new Dictionary<string, object?> { ["Name"] = "<b>&" });
        Assert.Contains("Hello, &lt;b&gt;&amp;!", html);
        Assert.DoesNotContain("<b>", html);
    }

    /// <summary>
    /// The dispatcher case: a component that suspends must still complete, inline, before the HTML
    /// is written — and the continuation must not need a thread pool that a worker isolate has none of.
    /// </summary>
    [Fact]
    public async Task WaitsForAsynchronousInitialization ()
    {
        var gate = new TaskCompletionSource();
        var rendering = Render<Awaiting>(new Dictionary<string, object?> { ["Gate"] = gate.Task });
        Assert.False(rendering.IsCompleted);
        gate.SetResult();
        Assert.Equal("<p>settled</p>", await rendering);
    }

    [Fact]
    public async Task ResolvesInjectedServices ()
    {
        var html = await Render<Injecting>(services: Services(s => s.AddSingleton<IGreeter, Greeter>()));
        Assert.Equal("<span>from di</span>", html);
    }

    /// <summary>
    /// The renderer's dispatcher runs work where it was queued, rather than posting it.
    /// </summary>
    /// <remarks>Blazor's default answers <c>CheckAccess()</c> from a synchronization context and
    /// posts through the thread pool when it has to; asserting the replacement is in place is what
    /// keeps a future refactor from quietly restoring the hop.</remarks>
    [Fact]
    public void RunsWorkInline ()
    {
        using var renderer = new WorkerHtmlRenderer(Services());
        Assert.True(renderer.Dispatcher.CheckAccess());
        var thread = Environment.CurrentManagedThreadId;
        var observed = 0;
        renderer.Dispatcher.InvokeAsync(() => observed = Environment.CurrentManagedThreadId).Wait();
        Assert.Equal(thread, observed);
    }

    /// <summary>A component is a result, and the result is what a route returns.</summary>
    [Fact]
    public async Task RendersThroughTheResultFactory ()
    {
        var result = TypedResults.Component<Greeting>(new Dictionary<string, object?> { ["Name"] = "x" });
        Assert.Equal(404, new ComponentHttpResult<Greeting>(null, 404).StatusCode);
        Assert.Null(result.StatusCode);
        // The factory only builds the result; ExecuteAsync needs an HttpContext, which the
        // AspNetCore suite's harness owns. Rendering is covered above through the same path.
        Assert.IsType<ComponentHttpResult<Greeting>>(result);
        await Task.CompletedTask;
    }
}
