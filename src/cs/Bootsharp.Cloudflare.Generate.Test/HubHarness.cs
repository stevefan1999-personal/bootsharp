using System.Collections.Immutable;
using Bootsharp.Cloudflare.Generate.SignalR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>Outcome of one <see cref="HubGenerator"/> run: what it refused and what it emitted.</summary>
internal sealed record HubRun(ImmutableArray<Diagnostic> Defects, string GeneratedCs)
{
    public string[] DefectIds => [.. Defects.Select(static d => d.Id).Order()];

    public string Message (string id) =>
        Defects.First(defect => defect.Id == id).GetMessage();
}

/// <summary>
/// Drives <see cref="HubGenerator"/> over a throwaway compilation. The SignalR surface it binds to
/// is a source stub for the same reason the worker surface is: the generator matches on namespace
/// and type name, so a stub under the real namespaces is exactly what it sees in a real app, and
/// the analyzer stays a netstandard2.0 assembly with no net10.0 package on its reference path.
/// </summary>
internal static class HubHarness
{
    private static readonly CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

    private static readonly MetadataReference[] references =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
    ];

    /// <summary>
    /// The shapes <c>HubRules</c> names, under their real namespaces: the hub base, the wire-name
    /// attribute, the dispatcher base the emitted class derives from, and the Durable Object base
    /// whose inherited transport surface the entrypoint generator injects.
    /// </summary>
    public const string Signalr = """
        namespace Microsoft.AspNetCore.SignalR
        {
            using System;
            using System.Threading.Tasks;

            public abstract class Hub : IDisposable
            {
                public virtual Task OnConnectedAsync() => Task.CompletedTask;
                public virtual Task OnDisconnectedAsync(Exception? exception) => Task.CompletedTask;
                public void Dispose() { }
            }

            [AttributeUsage(AttributeTargets.Method)]
            public class HubMethodNameAttribute(string name) : Attribute
            {
                public string Name { get; } = name;
            }
        }

        namespace Bootsharp.Cloudflare.SignalR
        {
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.SignalR;

            public abstract class HubDispatcher<THub> where THub : Hub
            {
                public abstract int Slot(string methodName);
                public abstract IReadOnlyList<Type> ParameterTypes(int slot);
                public abstract ValueTask<object?> Invoke(int slot, THub hub, object?[] args);
                public abstract THub Create();
                public abstract IReadOnlyList<string> MethodNames { get; }
            }

            [AttributeUsage(AttributeTargets.Class, Inherited = false)]
            public sealed class HubRouteAttribute(string pathPrefix) : Attribute
            {
                public string PathPrefix { get; } = pathPrefix;
            }

            public abstract class HubDurableObject<THub, TEnv> : global::Bootsharp.Cloudflare.DurableObject<TEnv>
                where THub : Hub where TEnv : class
            {
                protected HubDurableObject(global::Bootsharp.Cloudflare.IDurableObjectState ctx, TEnv env)
                    : base(ctx, env) { }

                protected abstract HubDispatcher<THub> CreateDispatcher();

                public Task Accept(string connectionId) => Task.CompletedTask;
                public Task Deliver(string connectionId, string message) => Task.CompletedTask;
                public Task Disconnect(string connectionId, string? reason) => Task.CompletedTask;
                public Task<int> Sweep() => Task.FromResult(0);
            }
        }
        """;

    public static HubRun Run (params string[] sources)
    {
        var trees = new[] { TestSources.Workers, Signalr }
            .Concat(sources)
            .Select(static source => CSharpSyntaxTree.ParseText(source, parseOptions));
        var compilation = CSharpCompilation.Create("HubGeneratorTests", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        IEnumerable<ISourceGenerator> generators = [new HubGenerator().AsSourceGenerator()];
        var driver = CSharpGeneratorDriver
            .Create(generators, parseOptions: parseOptions)
            .RunGenerators(compilation);
        var result = driver.GetRunResult().Results.Single();
        return new HubRun(result.Diagnostics, string.Join("\n",
            result.GeneratedSources.Select(static source => source.SourceText.ToString())));
    }
}
