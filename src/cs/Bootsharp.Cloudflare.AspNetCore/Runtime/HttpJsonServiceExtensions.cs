using Microsoft.AspNetCore.Http.Json;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures the JSON options body binding and JSON results resolve through.
/// </summary>
/// <remarks>
/// Named and shaped after upstream's call of the same name, because it is the one an app author
/// already has in their fingers. The implementation is not upstream's: there is no options pipeline
/// here — <c>WebApplicationBuilder</c> registers one <see cref="JsonOptions"/> instance behind
/// <c>IOptions&lt;JsonOptions&gt;</c>, and this configures that instance in place. Registration
/// order therefore does not matter, and an app no longer has to know about <c>Options.Create</c> to
/// join its <c>JsonSerializerContext</c> to the resolver chain.
/// </remarks>
public static class HttpJsonServiceExtensions
{
    /// <summary>Configures the application's HTTP JSON options.</summary>
    public static IServiceCollection ConfigureHttpJsonOptions (this IServiceCollection services,
        Action<JsonOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);
        configureOptions(Options(services));
        return services;
    }

    /// <summary>The single options instance, registered on first use.</summary>
    /// <remarks>The fallback registration is what makes this usable on a bare
    /// <see cref="ServiceCollection"/> — a test, or an app that never went through
    /// <c>WebApplication.CreateSlimBuilder</c>.</remarks>
    private static JsonOptions Options (IServiceCollection services)
    {
        foreach (var descriptor in services)
            if (descriptor.ServiceType == typeof(Microsoft.Extensions.Options.IOptions<JsonOptions>)
                && descriptor.ImplementationInstance is Microsoft.Extensions.Options.IOptions<JsonOptions> registered)
                return registered.Value;
        var options = new JsonOptions();
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<JsonOptions>>(
            Microsoft.Extensions.Options.Options.Create(options));
        return options;
    }
}
