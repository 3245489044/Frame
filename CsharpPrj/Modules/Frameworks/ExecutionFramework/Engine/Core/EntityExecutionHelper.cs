using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Systems;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Engine;

public sealed class EntityExecutionHelper
{
    private readonly IIntentSystem? _system;
    private readonly IntentSystemRouter _router;
    private readonly Rge.Unified.Resolution.ReferenceResolver _resolver;

    public EntityExecutionHelper(
        IntentSystemRouter? router = null,
        Rge.Unified.Resolution.ReferenceResolver? resolver = null,
        IIntentSystem? system = null)
    {
        if (router == null && system == null)
            throw new ArgumentException("router 与 system 至少提供一个。", nameof(router));

        _router = router ?? new IntentSystemRouter();
        _resolver = resolver ?? new Rge.Unified.Resolution.ReferenceResolver();
        _system = system;
    }

    public async Task<IntentResult> ExecuteEntityAsync(
        RgeEntity child,
        ExecutionGraph graph,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parameters = _resolver.ResolveAll(graph, child, out var errors);

        if (errors.Count > 0)
            return IntentResult.Failure(string.Join("; ", errors));

        var system = _system ?? _router.Route(child.StepType);
        if (system == null)
            return IntentResult.Failure($"未注册的 StepType: {child.StepType}");

        var result = await system.ExecuteAsync(child, parameters, graph, cancellationToken);

        if (!result.IsSuccess)
            return IntentResult.Failure(result.ErrorMessage ?? "子实体执行失败");

        child.Value = result.Value;
        if (result.Outputs != null)
        {
            foreach (var kvp in result.Outputs)
                child.Outputs[kvp.Key] = kvp.Value;
        }

        return result;
    }
}