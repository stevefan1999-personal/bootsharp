using Bootsharp;
using Bootsharp.Inject;
using Cloudflare.Backend;
using Cloudflare.Backend.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

[assembly: Export(typeof(IWorker), typeof(IActorRuntime))]

var holder = new AppHolder();
var builder = WebApplication.CreateSlimBuilder();
builder.Services.AddSingleton<IActorRuntime, ActorRuntime>();
builder.Services.AddSingleton<SiteService>();
builder.Services.AddSingleton<IWorker>(sp =>
    new Worker(holder.App ?? throw new InvalidOperationException("WebApplication not built.")));
builder.Services.AddBootsharp();
var app = builder.Build();
holder.App = app;
app.MapCloudflare();
app.Services.RunBootsharp();

internal sealed class AppHolder
{
    public WebApplication? App { get; set; }
}

public static class Prefs
{
    [RenameModule]
    public static string RenameModule(Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember(MemberInfo member, string @default)
    {
        if (member.DeclaringType == typeof(ICloudflareEnv))
            return member.Name;
        return @default;
    }
}
