using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// The conventions an app writes after a <c>Map*</c> call: naming, metadata, filters.
/// </summary>
/// <remarks>
/// These reach the endpoint because <c>RouteHandlerServices</c> applies conventions lazily, when the
/// endpoint table is built rather than when <c>Map*</c> returned — so a convention registered on the
/// returned builder is part of the endpoint, and a filter registered there is inside the request
/// delegate the endpoint carries.
/// </remarks>
public class EndpointConventionTests
{
    /// <summary>The endpoint one registration builds, conventions applied.</summary>
    private static Endpoint Built (Action<RouteHandlerBuilder> register)
    {
        var app = Worker.App(app => register(app.Says("/a", "handler")));
        app.Run();
        return app.DataSources.Single().Endpoints.Single();
    }

    [Fact]
    public void WithNameAddsBothNameMetadataKinds ()
    {
        var endpoint = Built(static builder => builder.WithName("todos"));
        Assert.Equal("todos", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName);
        Assert.Equal("todos", endpoint.Metadata.GetMetadata<IRouteNameMetadata>()!.RouteName);
    }

    [Fact]
    public void WithMetadataAppendsInOrder ()
    {
        var endpoint = Built(static builder => builder.WithMetadata("first", "second"));
        Assert.Equal(["first", "second"], endpoint.Metadata.OfType<string>());
    }

    [Fact]
    public void WithDisplayNameReplacesTheDefault () =>
        Assert.Equal("named", Built(static builder => builder.WithDisplayName("named")).DisplayName);

    /// <summary>The overload that composes on the name the endpoint already had.</summary>
    [Fact]
    public void WithDisplayNameCanReadTheNameItReplaces () =>
        Assert.Equal("/a!", Built(static builder =>
            builder.WithDisplayName(static endpoint => endpoint.DisplayName + "!")).DisplayName);

    [Fact]
    public void ConventionsChain ()
    {
        var endpoint = Built(static builder => builder.WithName("todos").WithDisplayName("named"));
        Assert.Equal("named", endpoint.DisplayName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IEndpointNameMetadata>());
    }

    [Fact]
    public void NullArgumentsAreRefused ()
    {
        var app = Worker.App(static app => app.Says("/a", "handler"));
        var builder = app.Says("/b", "handler");
        Assert.Throws<ArgumentNullException>(() => builder.WithName(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithMetadata(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithDisplayName((string)null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithDisplayName((Func<EndpointBuilder, string>)null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddEndpointFilter((IEndpointFilter)null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddEndpointFilterFactory(null!));
    }
}

/// <summary>
/// Endpoint filters, through the three registration shapes that need no reflection.
/// </summary>
/// <remarks>
/// A filter runs when something folds <see cref="EndpointBuilder.FilterFactories"/> into an
/// invocation chain — which is what the generated request delegate does, and what these do by hand,
/// since the tests in this project register endpoints without the generator. The generic
/// <c>AddEndpointFilter&lt;TFilter&gt;()</c> is deliberately absent from the package:
/// <c>ActivatorUtilities</c> picks a constructor by reflection.
/// </remarks>
public class EndpointFilterRegistrationTests
{
    private sealed class Tagging (string tag, List<string> log) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync (EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            log.Add($"enter {tag}");
            var result = await next(context);
            log.Add($"exit {tag}");
            return result;
        }
    }

    /// <summary>
    /// The builder the endpoint was actually built from, captured by a convention of our own
    /// registered last — the endpoint itself does not expose it.
    /// </summary>
    private static EndpointBuilder Building (Action<RouteHandlerBuilder> register)
    {
        EndpointBuilder? captured = null;
        var app = Worker.App(app =>
        {
            var builder = app.Says("/a", "handler");
            register(builder);
            builder.Add(endpoint => captured = endpoint);
        });
        app.Run();
        _ = app.DataSources.Single().Endpoints;
        return captured!;
    }

    /// <summary>Folds the registered factories the way the generated request delegate does.</summary>
    /// <param name="log">The same list the filters under test append to, so the recorded order is
    /// one sequence rather than two.</param>
    /// <param name="register">The registration whose filters are being exercised.</param>
    private static async Task<object?> Run (List<string> log, Action<RouteHandlerBuilder> register)
    {
        var builder = Building(register);
        EndpointFilterDelegate invocation = _ =>
        {
            log.Add("handler");
            return ValueTask.FromResult<object?>("body");
        };
        var context = new EndpointFilterFactoryContext {
            MethodInfo = ((Delegate)Nothing).Method,
            ApplicationServices = builder.ApplicationServices
        };
        for (var index = builder.FilterFactories.Count - 1; index >= 0; index--)
            invocation = builder.FilterFactories[index](context, invocation);
        using var httpContext = Worker.Context();
        return await invocation(new DefaultEndpointFilterInvocationContext(httpContext));
    }

    private static void Nothing () { }

    /// <summary>A filter instance wraps the handler.</summary>
    [Fact]
    public async Task FilterInstanceWrapsTheInvocation ()
    {
        var log = new List<string>();
        Assert.Equal("body", await Run(log, builder => builder.AddEndpointFilter(new Tagging("one", log))));
        Assert.Equal(["enter one", "handler", "exit one"], log);
    }

    /// <summary>A filter written as a delegate is the same rung with less ceremony.</summary>
    [Fact]
    public async Task DelegateFilterWrapsTheInvocation ()
    {
        var log = new List<string>();
        await Run(log, builder => builder.AddEndpointFilter(async (context, next) =>
        {
            log.Add("enter delegate");
            var result = await next(context);
            log.Add("exit delegate");
            return result;
        }));
        Assert.Equal(["enter delegate", "handler", "exit delegate"], log);
    }

    /// <summary>Filters run outside-in in registration order, as ASP.NET Core's do.</summary>
    [Fact]
    public async Task FiltersRunOutsideInInRegistrationOrder ()
    {
        var log = new List<string>();
        await Run(log, builder => builder
            .AddEndpointFilter(new Tagging("one", log))
            .AddEndpointFilter(new Tagging("two", log)));
        Assert.Equal(["enter one", "enter two", "handler", "exit two", "exit one"], log);
    }

    /// <summary>A factory may decline this endpoint by returning the invocation it was handed.</summary>
    [Fact]
    public async Task FactoryCanOptOutForAnEndpoint ()
    {
        var log = new List<string>();
        Assert.Equal("body", await Run(log, static builder =>
            builder.AddEndpointFilterFactory(static (_, next) => next)));
        Assert.Equal(["handler"], log);
    }

    /// <summary>The factory sees the handler's shape, which is why it is the rung underneath.</summary>
    [Fact]
    public async Task FactorySeesTheFactoryContext ()
    {
        EndpointFilterFactoryContext? seen = null;
        await Run([], builder => builder.AddEndpointFilterFactory((context, next) =>
        {
            seen = context;
            return next;
        }));
        Assert.NotNull(seen);
        Assert.NotNull(seen.MethodInfo);
    }

    /// <summary>A filter may answer without ever calling the handler.</summary>
    [Fact]
    public async Task FilterCanShortCircuit ()
    {
        var log = new List<string>();
        Assert.Equal("short", await Run(log, static builder =>
            builder.AddEndpointFilter(static (_, _) => ValueTask.FromResult<object?>("short"))));
        Assert.Empty(log);
    }
}
