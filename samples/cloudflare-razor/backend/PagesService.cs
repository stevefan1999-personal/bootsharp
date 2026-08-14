using Cloudflare.Razor.Pages;
using Microsoft.Extensions.Logging;
using HomePage = Cloudflare.Razor.Pages.Home;

namespace Cloudflare.Razor;

/// <summary>Handlers that return a compiled <c>.cshtml</c> page or a JSON snapshot.</summary>
public sealed class PagesService (ILogger<PagesService> logger)
{
    public IResult Home (string? name, string? probe)
    {
        logger.LogInformation("home rendered for {Name}", name ?? "(anonymous)");
        return TypedResults.Html(html => HomePage.Render(html, new HomeModel {
            Runtime = ".NET " + System.Environment.Version,
            Environment = WorkerContext.Env.ENVIRONMENT,
            Name = name,
            Probe = probe
        }));
    }

    public IResult Health () => TypedResults.Ok(new Health(
        Ok: true,
        Runtime: ".NET " + System.Environment.Version,
        Environment: WorkerContext.Env.ENVIRONMENT,
        Page: "Cloudflare.Razor.Pages.Home"));
}
