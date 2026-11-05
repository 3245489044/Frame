using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Engine;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Systems;

public sealed class LoopSystem : IIntentSystem
{
    private readonly IntentSystemRouter _router;
    private readonly EntityExecutionHelper _helper;

    public LoopSystem(IntentSystemRouter router, Rge.Unified.Resolution.ReferenceResolver? resolver = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _helper = new EntityExecutionHelper(router, resolver);
    }

    public string StepType => "Loop";

    public async Task<IntentResult> ExecuteAsync(
        RgeEntity entity,
        IReadOnlyDictionary<string, object?> resolvedParameters,
        ExecutionGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.SubGraph == null || entity.SubGraph.Entities.Count == 0)
            return IntentResult.Success(null);

        var maxIterations = GetMaxIterations(entity);

        object? lastResult = entity.SubGraph.RootInput;
        for (int i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            entity.SubGraph.RootInput = lastResult;

            var subResult = await ExecuteSubGraphAsync(entity.SubGraph, cancellationToken);
            if (!subResult.IsSuccess)
                return subResult;

            lastResult = subResult.Value;
        }

        return IntentResult.Success(lastResult, new Dictionary<string, object?>
        {
            ["iterations"] = maxIterations,
            ["lastResult"] = lastResult
        });
    }

    private async Task<IntentResult> ExecuteSubGraphAsync(
        ExecutionGraph subGraph,
        CancellationToken cancellationToken)
    {
        var order = GraphTopology.Sort(subGraph);
        var cyclic = GraphTopology.GetCyclicEntities(subGraph, order);
        if (cyclic.Count > 0)
        {
            var cyclicNames = string.Join("; ", cyclic.Select(c => c.Name));
            return IntentResult.Failure($"循环依赖，无法调度: {cyclicNames}");
        }

        object? lastValue = null;

        foreach (var child in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _helper.ExecuteEntityAsync(child, subGraph, cancellationToken);
            if (!result.IsSuccess)
                return IntentResult.Failure(result.ErrorMessage ?? "子实体执行失败");

            lastValue = result.Value;
        }

        return IntentResult.Success(lastValue);
    }

    private static int GetMaxIterations(RgeEntity entity)
    {
        var maxVal = entity.Components.Get("MaxIterations");
        if (maxVal != null && int.TryParse(maxVal.ToString(), out var parsed))
            return Math.Max(1, parsed);

        var metadata = entity.Components.Get<Dictionary<string, object?>>(ComponentKeys.Metadata);
        if (metadata != null && metadata.TryGetValue("MaxIterations", out var metaVal)
            && metaVal != null && int.TryParse(metaVal.ToString(), out parsed))
        {
            return Math.Max(1, parsed);
        }

        return 10;
    }
}