using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// The hub-dispatch generator. Upstream has no generator at all
/// and reaches <c>MethodInfo.Invoke</c> even on its "AOT-compatible" path; the whole point of these
/// assertions is that what is emitted here is a compile-time constant — a name switch, static type
/// arrays and direct calls — with no reflection anywhere on it.
/// </summary>
public class HubDispatchTests
{
    [Fact]
    public void EmitsADispatcherNamedAfterTheHub ()
    {
        var run = HubHarness.Run(Hub("public string Echo(string message) => message;"));
        Assert.Empty(run.Defects);
        Assert.Contains("public sealed class ChatHubDispatcher", run.GeneratedCs);
        Assert.Contains("global::Bootsharp.Cloudflare.SignalR.HubDispatcher<global::App.ChatHub>", run.GeneratedCs);
    }

    [Fact]
    public void SlotLookupIsCaseInsensitiveAsUpstreamsDictionaryIs ()
    {
        var run = HubHarness.Run(Hub("public string Echo(string message) => message;"));
        Assert.Contains("StringComparison.OrdinalIgnoreCase", run.GeneratedCs);
    }

    [Fact]
    public void HubMethodNameBecomesTheWireName ()
    {
        var run = HubHarness.Run(Hub("""
            [Microsoft.AspNetCore.SignalR.HubMethodName("client.ping")]
            public string Ping() => "pong";
            """));
        Assert.Empty(run.Defects);
        Assert.Contains("\"client.ping\"", run.GeneratedCs);
        // The CLR name must not also be dispatchable, or two wire names would reach one method and
        // renaming would not actually rename anything.
        Assert.DoesNotContain("\"Ping\"", run.GeneratedCs);
    }

    [Fact]
    public void ParameterTypesAreStaticArraysRatherThanReflection ()
    {
        var run = HubHarness.Run(Hub("public int Add(int left, int right) => left + right;"));
        Assert.Contains("new global::System.Type[] { typeof(int), typeof(int) }", run.GeneratedCs);
        Assert.DoesNotContain("MethodInfo", run.GeneratedCs);
        Assert.DoesNotContain("GetMethod", run.GeneratedCs);
    }

    [Fact]
    public void ArgumentsAreCastToTheirDeclaredTypesRatherThanConverted ()
    {
        var run = HubHarness.Run(Hub("public int Add(int left, int right) => left + right;"));
        Assert.Contains("hub.Add((int)args[0]!, (int)args[1]!)", run.GeneratedCs);
    }

    [Fact]
    public void TaskReturningMethodsAreAwaitedInsideTheDispatcher ()
    {
        var run = HubHarness.Run(Hub(
            "public System.Threading.Tasks.Task<string> Slow() => System.Threading.Tasks.Task.FromResult(\"x\");"));
        Assert.Contains("return await hub.Slow();", run.GeneratedCs);
    }

    [Fact]
    public void VoidMethodsReturnNullRatherThanAValue ()
    {
        var run = HubHarness.Run(Hub("public void Notify(string message) { }"));
        Assert.Contains("hub.Notify((string)args[0]!);", run.GeneratedCs);
        Assert.Contains("return null;", run.GeneratedCs);
    }

    [Fact]
    public void HubWithNoDispatchableMethodStillEmitsADispatcher ()
    {
        // Legal: a hub may exist only for OnConnectedAsync. Emitting nothing would leave the app's
        // CreateDispatcher override naming a type that does not exist.
        var run = HubHarness.Run(Hub(""));
        Assert.Empty(run.Defects);
        Assert.Contains("public sealed class ChatHubDispatcher", run.GeneratedCs);
    }

    [Fact]
    public void OverriddenHubCallbacksAreNotDispatchable ()
    {
        var run = HubHarness.Run(Hub("""
            public override System.Threading.Tasks.Task OnConnectedAsync() =>
                System.Threading.Tasks.Task.CompletedTask;
            """));
        Assert.Empty(run.Defects);
        Assert.DoesNotContain("OnConnectedAsync", run.GeneratedCs);
    }

    [Fact]
    public void DuplicateWireNameIsRefused ()
    {
        // The table is case-insensitive, so two overloads are one wire name and one of them would
        // simply never run — the worst possible silent choice.
        var run = HubHarness.Run(Hub("""
            public string Send(string message) => message;
            public string send(int count) => count.ToString();
            """));
        Assert.Contains("CFW041", run.DefectIds);
        Assert.Contains("resolves case-insensitively", run.Message("CFW041"));
    }

    [Fact]
    public void GenericHubMethodIsRefused ()
    {
        var run = HubHarness.Run(Hub("public T Echo<T>(T value) => value;"));
        Assert.Contains("CFW042", run.DefectIds);
    }

    [Fact]
    public void ByRefParameterIsRefused ()
    {
        var run = HubHarness.Run(Hub("public void Fill(ref int value) { value = 1; }"));
        Assert.Contains("CFW043", run.DefectIds);
    }

    [Fact]
    public void StreamingReturnIsRefusedByNameRatherThanSerializedAsAnObject ()
    {
        var run = HubHarness.Run(Hub("""
            public System.Collections.Generic.IAsyncEnumerable<int> Counter() => throw new System.NotImplementedException();
            """));
        Assert.Contains("CFW044", run.DefectIds);
        Assert.Contains("their own package", run.Message("CFW044"));
    }

    [Fact]
    public void HubWithoutAParameterlessConstructorIsRefused ()
    {
        var run = HubHarness.Run("""
            namespace App;
            public class ChatHub(string room) : Microsoft.AspNetCore.SignalR.Hub
            {
                public string Room() => room;
            }
            """);
        Assert.Contains("CFW045", run.DefectIds);
        Assert.Empty(run.GeneratedCs);
    }

    [Fact]
    public void ARefusedShapeEmitsNothingAtAll ()
    {
        // rule: total dispatch or a diagnostic. A partially emitted table would route
        // some calls and silently drop others.
        var run = HubHarness.Run(Hub("public T Echo<T>(T value) => value;"));
        Assert.Empty(run.GeneratedCs);
    }

    [Fact]
    public void PrimitivePayloadsNeedNoJsonContext ()
    {
        var run = HubHarness.Run(Hub("""
            public string Echo(string message) => message;
            public int Add(int left, int right) => left + right;
            public System.Guid Id() => System.Guid.Empty;
            """));
        Assert.DoesNotContain("CFW040", run.DefectIds);
    }

    [Fact]
    public void ACustomPayloadWithNoJsonContextIsWarnedAbout ()
    {
        // Reflection-based serialization is off under NativeAOT, so an undeclared payload does not
        // degrade — it throws at the first client message.
        var run = HubHarness.Run("""
            namespace App;
            public record Message(string Text);
            public class ChatHub : Microsoft.AspNetCore.SignalR.Hub
            {
                public Message Echo(Message message) => message;
            }
            """);
        Assert.Contains("CFW040", run.DefectIds);
        Assert.Contains("[JsonSerializable(typeof(App.Message))]", run.Message("CFW040"));
    }

    [Fact]
    public void ACustomPayloadDeclaredOnAJsonContextIsAccepted ()
    {
        var run = HubHarness.Run("""
            namespace App;
            public record Message(string Text);

            [System.Text.Json.Serialization.JsonSerializable(typeof(Message))]
            public partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

            public class ChatHub : Microsoft.AspNetCore.SignalR.Hub
            {
                public Message Echo(Message message) => message;
            }
            """);
        Assert.DoesNotContain("CFW040", run.DefectIds);
    }

    [Fact]
    public void AnAbstractHubIsNotDispatched ()
    {
        var run = HubHarness.Run("""
            namespace App;
            public abstract class BaseHub : Microsoft.AspNetCore.SignalR.Hub
            {
                public string Echo(string message) => message;
            }
            """);
        Assert.Empty(run.GeneratedCs);
    }

    [Fact]
    public void MethodsInheritedFromAnIntermediateClassAreDispatchable ()
    {
        // A hub may split its surface across an intermediate class, and upstream's
        // HubReflectionHelper.GetHubMethods walks the same chain.
        var run = HubHarness.Run("""
            namespace App;
            public abstract class BaseHub : Microsoft.AspNetCore.SignalR.Hub
            {
                public string Inherited() => "yes";
            }
            public class ChatHub : BaseHub
            {
                public string Own() => "yes";
            }
            """);
        Assert.Contains("\"Inherited\"", run.GeneratedCs);
        Assert.Contains("\"Own\"", run.GeneratedCs);
    }

    private static string Hub (string body) => $$"""
        namespace App;
        public class ChatHub : Microsoft.AspNetCore.SignalR.Hub
        {
            {{body}}
        }
        """;
}
