using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// <c>ConfigureHttpJsonOptions</c>: the seam an app joins its <c>JsonSerializerContext</c> through.
/// </summary>
/// <remarks>
/// It exists because the alternative was re-registering <c>IOptions&lt;JsonOptions&gt;</c> with
/// <c>Options.Create(...)</c> and relying on last-registration-wins — which works, but requires an
/// app to know how this package stores its options. Configuring the one instance in place is
/// order-independent, which is what these pin.
/// </remarks>
public class ConfigureHttpJsonOptionsTests
{
    [Fact]
    public void ConfiguresTheInstanceTheBuilderAlreadyRegistered ()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.ConfigureHttpJsonOptions(static options => options.AddContext(TestJson.Default));
        var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.NotNull(options.GetTypeInfo<Todo>());
    }

    /// <summary>Two calls add to the same chain rather than the second replacing the first.</summary>
    [Fact]
    public void CallsAccumulate ()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.ConfigureHttpJsonOptions(static options => options.AddContext(TestJson.Default));
        builder.Services.ConfigureHttpJsonOptions(static options =>
            options.SerializerOptions.WriteIndented = true);
        var options = builder.Build().Services.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.NotNull(options.GetTypeInfo<Todo>());
        Assert.True(options.SerializerOptions.WriteIndented);
    }

    /// <summary>On a bare collection it registers the options it was asked to configure.</summary>
    [Fact]
    public void RegistersOptionsWhenNothingHas ()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(static options => options.AddContext(TestJson.Default));
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.NotNull(options.GetTypeInfo<Todo>());
    }

    /// <summary>What the seam buys, end to end: a JSON result serializes without Options.Create.</summary>
    [Fact]
    public async Task ResultSerializesThroughTheConfiguredContext ()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.ConfigureHttpJsonOptions(static options => options.AddContext(TestJson.Default));
        var app = builder.Build();
        RouteHandlerServices.Map(app, "/todo",
            static context => TypedResults.Ok(new Todo(1, "write it down")).ExecuteAsync(context), null);
        var answer = await app.Send("GET", "https://w.dev/todo");
        Assert.Equal(200, answer.Status);
        Assert.Equal("""{"id":1,"title":"write it down"}""", answer.Body);
    }

    [Fact]
    public void NullArgumentsAreRefused ()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).ConfigureHttpJsonOptions(static _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().ConfigureHttpJsonOptions(null!));
    }
}
