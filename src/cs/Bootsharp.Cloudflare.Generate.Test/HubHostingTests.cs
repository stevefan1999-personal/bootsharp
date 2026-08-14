using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// The hosting half: a Durable Object that hosts a hub inherits its transport surface from
/// <c>HubDurableObject&lt;THub, TEnv&gt;</c> rather than declaring it, and BOTH front ends read
/// declared members only. Without the injection, the C# dispatch and the ESM module would each be
/// missing the four methods the shipped <c>js/signalr.mjs</c> calls — and nothing but a runtime
/// TypeError would say so. Running both halves in one assertion is what keeps them agreeing.
/// </summary>
public class HubHostingTests
{
    private const string chatRoom = """
        namespace Cloudflare.Backend;

        using Bootsharp.Cloudflare;
        using Bootsharp.Cloudflare.SignalR;
        using Microsoft.AspNetCore.SignalR;

        public class ChatHub : Hub
        {
            public string Echo(string message) => message;
        }

        public class ChatRoom(IDurableObjectState ctx, ICloudflareEnv env)
            : HubDurableObject<ChatHub, ICloudflareEnv>(ctx, env)
        {
            protected override HubDispatcher<ChatHub> CreateDispatcher() => null!;
        }
        """;

    [Fact]
    public void InheritedTransportMethodsAreProjectedIntoTheEmittedModule ()
    {
        var run = Run();
        foreach (var method in (string[])["accept", "deliver", "disconnect", "sweep"])
            Assert.Contains($"async {method}(", run.GeneratedJs);
    }

    [Fact]
    public void InheritedTransportMethodsAreProjectedIntoTheGeneratedCsDispatch ()
    {
        var run = Run();
        foreach (var method in (string[])["\"accept\"", "\"deliver\"", "\"disconnect\"", "\"sweep\""])
            Assert.Contains(method, run.GeneratedCs);
    }

    [Fact]
    public void TheStubWrapperForwardsTheTransportMethodsToo ()
    {
        // An app reaching another Durable Object through a namespace binding calls these by name on
        // the wrapper, so a stub without them would compile and fail at the call.
        var run = Run();
        Assert.Contains("wrap.deliver = (...args) => stub[\"deliver\"](...args);", run.GeneratedJs);
    }

    [Fact]
    public void ADurableObjectThatHostsNoHubGetsNoTransportMethods ()
    {
        var run = GeneratorHarness.RunActors(HubHarness.Signalr, """
            namespace Cloudflare.Backend;
            using Bootsharp.Cloudflare;
            public class Counter(IDurableObjectState ctx, ICloudflareEnv env) : DurableObject<ICloudflareEnv>(ctx, env)
            {
                public int Bump() => 1;
            }
            """);
        Assert.DoesNotContain("async deliver(", run.GeneratedJs);
    }

    [Fact]
    public void TheProjectionCompiles ()
    {
        Assert.Empty(Run().CompilationErrors);
    }

    private static GeneratorRun Run () => GeneratorHarness.RunActors(HubHarness.Signalr, chatRoom);
}
