namespace Llvm.Exercise;

/// <summary>Response snapshot the emitted worker module turns back into a workerd Response.</summary>
public sealed record ExerciseResponse(int Status, string HeadersJson, string Body);

/// <summary>The surface the emitted module calls into.</summary>
public interface IWorker
{
    Task<ExerciseResponse> Fetch (IJsRequest request, IExerciseEnv env);
}
