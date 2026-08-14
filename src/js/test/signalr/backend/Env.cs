namespace SignalR.Harness;

/// <summary>
/// The harness worker's wrangler bindings. The Durable Object namespace is projected so the worker
/// can route a connection to its room; KV is here only because the env needs at least one binding
/// to be worth adapting.
/// </summary>
[WorkerEnv]
public interface IHarnessEnv
{
    IChatRoomNamespace CHAT { get; }
    string ENVIRONMENT { get; }
}

/// <summary>wrangler <c>CHAT</c> binding: TS <c>DurableObjectNamespace&lt;ChatRoom&gt;</c>.</summary>
[JSHandle(Scope = HandleScope.Isolate)]
public interface IChatRoomNamespace
{
    DurableObjectId NewUniqueId();
    DurableObjectId IdFromName(string name);
    DurableObjectId IdFromString(string id);
    IChatRoomStub Get(string id);
    IChatRoomStub GetByName(string name);
}

/// <summary>
/// TS <c>DurableObjectStub&lt;ChatRoom&gt;</c>. The four transport methods are inherited from
/// <c>HubDurableObject</c> and injected into the projection by the shared rules, which is exactly
/// the thing this harness exists to prove end to end.
/// </summary>
public interface IChatRoomStub
{
    string Id { get; }
    string? Name { get; }
    Task<string> NegotiateResponse(string connectionId);
    Task Accept(string connectionId);
    Task Deliver(string connectionId, string message);
    Task Disconnect(string connectionId, string? reason);
    Task<int> Sweep();
}
