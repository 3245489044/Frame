using Rge.Unified.Models;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Reference;

public static class ExecutionGraphAdapter
{
    public const string RootInputMetadataKey = "rootInput";


    public static ExecutionGraph FromPlan(TConfig.ExecutionPlan plan, object? rootInput = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        rootInput ??= plan.Metadata.TryGetValue(RootInputMetadataKey, out var ri) ? ri : null;
        var graph = new ExecutionGraph { GraphId = plan.Id, RootInput = rootInput };

        // 工作流级输出聚合映射（outputMapping）注入图，供 RgeGraphScheduler.ApplyOutputMapping 最终聚合。
        if (plan.Metadata.TryGetValue("OutputMapping", out var om) && om is Dictionary<string, string> outMap)
        {
            foreach (var kvp in outMap)
                graph.OutputMapping[kvp.Key] = kvp.Value;
        }

        int index = 0;
        string? prevUid = null;

        foreach (var step in plan.Steps)
        {
            if (step is null || !step.IsEnabled || step.IsSkipped)
                continue;
            if (string.IsNullOrWhiteSpace(step.FunctionName) && step.Children.Count == 0 && step.FalseChildren.Count == 0)
                continue;

            var uid = $"step-{index++}";
            var entity = BuildEntity(uid, step);
            graph.AddEntity(entity);

            if (prevUid != null)
                graph.AddEdge(prevUid, uid);
            prevUid = uid;
        }

        return graph;
    }


    public static ExecutionGraph FromSteps(
        string graphId,
        IEnumerable<TConfig.StepDefinition> steps,
        object? rootInput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId, nameof(graphId));
        ArgumentNullException.ThrowIfNull(steps);

        var graph = new ExecutionGraph { GraphId = graphId, RootInput = rootInput };

        int index = 0;
        string? prevUid = null;
        foreach (var step in steps)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.FunctionName))
                continue;

            var uid = $"step-{index++}";
            graph.AddEntity(BuildEntity(uid, step));

            if (prevUid != null)
                graph.AddEdge(prevUid, uid, targetParam: null, sourceKey: null);
            prevUid = uid;
        }

        return graph;
    }


    public static RgeEntity CreateEntity(
        string uid,
        string name,
        string? funcRef = null,
        string stepType = ComponentKeys.DefaultStepType,
        string? outputKey = null)
    {
        var entity = new RgeEntity
        {
            Uid = uid,
            Name = name
        };

        if (!string.IsNullOrEmpty(funcRef))
            entity.WithComponent(ComponentKeys.FuncRef, funcRef);
        entity.WithComponent(ComponentKeys.StepType, stepType);
        if (!string.IsNullOrEmpty(outputKey))
            entity.WithComponent(ComponentKeys.OutputKey, outputKey);

        return entity;
    }

    public static RgeEntity WithParamsFromDictionary(
        RgeEntity entity,
        IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var kvp in parameters)
        {
            if (kvp.Value is string path && ReferenceResolverBridge.IsReferencePath(path))
            {
                entity.WithParamRef(kvp.Key, path);
            }
            else
            {
                entity.WithParamRef(kvp.Key, $"input.{kvp.Key}");
            }
        }
        return entity;
    }


    private static RgeEntity BuildEntity(string uid, TConfig.StepDefinition step)
    {
        var entity = new RgeEntity
        {
            Uid = uid,
            Name = step.Name ?? step.FunctionName ?? uid
        };

        if (!string.IsNullOrEmpty(step.FunctionName))
            entity.WithComponent(ComponentKeys.FuncRef, step.FunctionName);
        entity.WithComponent(ComponentKeys.StepType, string.IsNullOrWhiteSpace(step.StepType)
            ? ComponentKeys.DefaultStepType
            : step.StepType);

        var outputKey = step.Metadata.TryGetValue("OutputKey", out var ok) ? ok?.ToString() : null;
        if (string.IsNullOrWhiteSpace(outputKey))
            outputKey = step.Name;
        if (!string.IsNullOrWhiteSpace(outputKey))
            entity.WithComponent(ComponentKeys.OutputKey, outputKey);

        if (step.Parameters is System.Collections.IDictionary dict)
        {
            foreach (var key in dict.Keys)
            {
                if (key is not string keyStr)
                    continue;

                var value = dict[keyStr];
                if (value is string refPath && ReferenceResolverBridge.IsReferencePath(refPath))
                {
                    entity.WithParamRef(keyStr, refPath);
                    continue;
                }

                entity.WithParamRef(keyStr, $"input.{keyStr}", required: false, fallback: value);
            }
        }

        if (step.TimeoutMs > 0 || step.RetryCount > 0)
        {
            entity.WithComponent(ComponentKeys.Policy, new ResiliencePolicy
            {
                RetryCount = step.RetryCount,
                RetryDelayMs = step.RetryDelayMs,
                TimeoutMs = step.TimeoutMs
            });
        }

        var subGraph = BuildSubGraph(uid, step);
        if (subGraph != null)
            entity.WithSubGraph(subGraph);

        ApplyCompositeMetadata(entity, step);

        return entity;
    }

    private static ExecutionGraph? BuildSubGraph(string hostUid, TConfig.StepDefinition step)
    {
        bool hasChildren = step.Children is { Count: > 0 };
        bool hasFalseChildren = step.FalseChildren is { Count: > 0 };
        if (!hasChildren && !hasFalseChildren)
            return null;

        var subGraph = new ExecutionGraph { GraphId = $"{hostUid}/sub" };
        int index = 0;

        if (hasChildren)
        {
            foreach (var child in step.Children)
            {
                if (child is null || !child.IsEnabled || child.IsSkipped)
                    continue;
                var uid = $"child-{index++}";
                subGraph.AddEntity(BuildEntity(uid, child));
            }
        }

        if (hasFalseChildren)
        {
            foreach (var child in step.FalseChildren)
            {
                if (child is null || !child.IsEnabled || child.IsSkipped)
                    continue;
                var uid = $"child-{index++}";
                subGraph.AddEntity(BuildEntity(uid, child));
            }
        }

        return subGraph.Entities.Count > 0 ? subGraph : null;
    }

    private static void ApplyCompositeMetadata(RgeEntity entity, TConfig.StepDefinition step)
    {
        var normalizedType = (step.StepType ?? string.Empty).Trim();

        Dictionary<string, object?>? metadata = null;

        if (normalizedType.Equals("Loop", StringComparison.OrdinalIgnoreCase))
        {
            int maxIterations = 10;
            if (step.LoopConfig?.MaxIterations > 0)
            {
                maxIterations = step.LoopConfig.MaxIterations;
            }
            else if (step.Metadata.TryGetValue("MaxIterations", out var metaVal)
                     && metaVal is int intVal && intVal > 0)
            {
                maxIterations = intVal;
            }

            if (maxIterations != 10)
            {
                (metadata ??= new()).Add("MaxIterations", maxIterations);
            }
        }
        else if (normalizedType.Equals("Condition", StringComparison.OrdinalIgnoreCase)
                 && step.FalseChildren is { Count: > 0 })
        {
            int trueBranchCount = step.Children?.Count ?? 0;
            if (step.Metadata.TryGetValue("TrueBranchCount", out var tbc))
            {
                trueBranchCount = tbc is int tbv ? tbv : 0;
            }
            (metadata ??= new()).Add("TrueBranchCount", trueBranchCount);
        }

        if (metadata != null)
            entity.WithComponent(ComponentKeys.Metadata, metadata);
    }
}
