namespace Interleave.Harness;

/// <summary>
/// The harness worker's wrangler bindings. Only KV is projected into C#: it is the "binding call"
/// a hub-method-shaped handler awaits in scenario 1, and it is deliberately NOT Durable Object
/// storage — workerd holds the actor input gate across a storage await
/// (<c>IoContext::awaitIoWithInputLock</c>, the only caller of which is <c>api/actor-state.c++</c>)
/// and releases it across every other kind of await. The Durable Object namespace is not declared
/// here because routing to the actor happens in the hand-written harness module, in JavaScript.
/// </summary>
[WorkerEnv]
public interface IHarnessEnv
{
    IKvNamespace KV { get; }
    string ENVIRONMENT { get; }
}
