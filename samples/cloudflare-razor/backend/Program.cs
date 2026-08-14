using Bootsharp;
using Bootsharp.Cloudflare.Logging;
using Cloudflare.Razor;
using Cloudflare.Razor.Notes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

[assembly: Export(typeof(IWorker))]
[assembly: Import(typeof(ILogSink))]

var holder = new AppHolder();
var builder = WebApplication.CreateSlimBuilder();
AddJsonLogging(builder.Services);
builder.Services.ConfigureHttpJsonOptions(options => options.AddContext(ApiJsonContext.Default));
builder.Services
    .AddCloudflareEnv()
    .AddFreeSqlNotes()
    .AddScoped<PagesService>();
builder.Services.AddSingleton<IWorker>(provider => new Worker(
    holder.App ?? throw new InvalidOperationException("WebApplication was not built."),
    provider.GetRequiredService<ILogger<Worker>>()));

var app = builder.Build();
holder.App = app;
app.MapGet("/", (PagesService pages, string? name, string? probe, string? flash) =>
    pages.Home(name, probe, flash));
app.MapPost("/notes", (PagesService pages, HttpRequest request) => pages.Create(request));
app.MapPost("/notes/{id:int}/delete", (PagesService pages, int id) => pages.Delete(id));
app.MapGet("/api/health", (PagesService pages) => pages.Health());
app.MapGet("/api/notes", (PagesService pages) => pages.ListNotes());
app.MapPost("/api/notes", (PagesService pages, NoteInput input) => pages.PostNote(input));
app.Run();

Modules.Exports.Values.Single(module => module.Handler == typeof(IWorker))
    .Factory(app.Services.GetRequiredService<IWorker>());

static void AddJsonLogging (IServiceCollection services)
{
    services.AddSingleton(typeof(ILogSink), _ =>
        (ILogSink)Modules.Imports[typeof(ILogSink)].Instance!);
    services.AddSingleton<ILoggerProvider>(provider =>
        new CloudflareJsonLoggerProvider(provider.GetRequiredService<ILogSink>()));
    services.AddSingleton<ILoggerFactory>(provider =>
        new LoggerFactory(provider.GetServices<ILoggerProvider>()));
    services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
}

internal sealed class AppHolder
{
    public WebApplication? App { get; set; }
}

public static class Prefs
{
    [RenameModule]
    public static string RenameModule (Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember (MemberInfo member, string @default) =>
        member.DeclaringType == typeof(IWorkerEnv) ? member.Name : @default;
}
