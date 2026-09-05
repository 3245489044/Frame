namespace Rge.Unified.Systems;

using Rge.Unified.Models;
using Rge.Unified.Resolution;

public sealed class IntentResult
{
    public bool IsSuccess { get; init; }

    public object? Value { get; init; }

    public IReadOnlyDictionary<string, object?>? Outputs { get; init; }

    public string? ErrorMessage { get; init; }

    public static IntentResult Success(object? value, IReadOnlyDictionary<string, object?>? outputs = null) =>
        new() { IsSuccess = true, Value = value, Outputs = outputs };

    public static IntentResult Failure(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}

public interface IIntentSystem
{
    string StepType { get; }

    Task<IntentResult> ExecuteAsync(
        Entity entity,
        IReadOnlyDictionary<string, object?> resolvedParameters,
        ExecutionGraph graph,
        CancellationToken cancellationToken = default);
}

public interface ILifecycleSystem
{
    Task OnGraphStartAsync(ExecutionGraph graph, CancellationToken cancellationToken = default);

    Task OnEntityCompleteAsync(Entity entity, IntentResult result, CancellationToken cancellationToken = default);

    Task OnGraphEndAsync(ExecutionGraph graph, CancellationToken cancellationToken = default);
}

public interface IReferenceSystem
{
    ResolveResult Resolve(ExecutionGraph graph, Entity current, string path);

    Dictionary<string, object?> ResolveAll(ExecutionGraph graph, Entity current, out List<string> errors);
}