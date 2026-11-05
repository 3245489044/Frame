using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Engine;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Systems;

public sealed class ParallelSystem : IIntentSystem
{
    private readonly EntityExecutionHelper _helper;

    public ParallelSystem(IntentSystemRouter router, Rge.Unified.Resolution.ReferenceResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        _helper = new EntityExecutionHelper(router, resolver);
    }

    public string StepType => "Parallel";

    public async Task<IntentResult> ExecuteAsync(
        RgeEntity entity,
        IReadOnlyDictionary<string, object?> resolvedParameters,
        ExecutionGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.SubGraph == null || entity.SubGraph.Entities.Count == 0)
            return IntentResult.Success(null);

        var layers = GraphTopology.SortByLayers(entity.SubGraph);
        if (layers.Sum(l => l.Count) < entity.SubGraph.Entities.Count)
        {
            var cyclic = GraphTopology.GetCyclicEntities(
                entity.SubGraph, layers.SelectMany(l => l).ToList());
            var cyclicNames = string.Join("; ", cyclic.Select(c => c.Name));
            return IntentResult.Failure($"循环依赖，无法调度: {cyclicNames}");
        }

        var values = new List<object?>();
        var outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tasks = layer
                .Select(child => _helper.ExecuteEntityAsync(child, entity.SubGraph!, cancellationToken))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            var failed = results.FirstOrDefault(r => !r.IsSuccess);
            if (failed != null)
                return IntentResult.Failure(failed.ErrorMessage ?? "并行子任务执行失败");

            for (int i = 0; i < results.Length; i++)
            {
                var result = results[i];
                values.Add(result.Value);
                if (result.Outputs != null)
                {
                    foreach (var kvp in result.Outputs)
                        outputs[kvp.Key] = kvp.Value;
                }
            }
        }

        return IntentResult.Success(values, outputs);
    }
}