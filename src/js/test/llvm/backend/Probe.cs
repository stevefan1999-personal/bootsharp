namespace Llvm.Exercise;

/// <summary>
/// The actor behind <see cref="IProbeStub"/>. Its state handle is constructed once and reused by
/// every later RPC, so a second successful <see cref="Increment"/> is also the proof that
/// <c>IDurableObjectState</c>'s isolate scope kept it out of the first invocation's disposal set.
/// </summary>
public sealed class Probe (IDurableObjectState ctx, IExerciseEnv env) : DurableObject<IExerciseEnv>(ctx, env)
{
    public async Task<int> Increment ()
    {
        var raw = await Ctx.Storage.Get("n");
        var next = (int.TryParse(raw, out var n) ? n : 0) + 1;
        await Ctx.Storage.Put("n", next.ToString());
        return next;
    }
}
