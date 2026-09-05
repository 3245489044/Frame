using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Reference;
using ExecutionFramework.Systems;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Engine;

public sealed class RgeGraphScheduler
{
    private readonly IIntentSystem? _system;
    private readonly IntentSystemRouter? _router;
    private readonly ReferenceResolverBridge _resolver;
    private readonly EntityExecutionHelper _entityHelper;

    public Func<CancellationToken, Task>? BeforeEntityExecuted { get; set; }

    /// <summary>
    /// 失败不中断开关：为 true 时，某步骤失败仅记录（FailedSteps）而不再跳过后继步骤，
    /// 对齐栈B 工作流语义（失败收集 + 继续执行后续步骤）；默认 false 保持 RGE 原 fail-fast 语义。
    /// </summary>
    public bool ContinueOnFailure { get; set; }

    public RgeGraphScheduler(
        IIntentSystem? system = null,
        IntentSystemRouter? router = null,
        ReferenceResolverBridge? resolver = null)
    {
        if (system == null && router == null)
            throw new ArgumentException("system 与 router 至少提供一个。", nameof(system));

        _system = system;
        _router = router;
        _resolver = resolver ?? new ReferenceResolverBridge();

        _entityHelper = new EntityExecutionHelper(
            router: router ?? new IntentSystemRouter(),
            resolver: _resolver.Inner,
            system: system);
    }

    public async Task<GraphExecutionResult> ExecuteAsync(
        ExecutionGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var result = new GraphExecutionResult();

        if (graph.Entities.Count == 0)
            return result;

        var failedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var executionOrder = GraphTopology.Sort(graph);
        var cyclicEntities = GraphTopology.GetCyclicEntities(graph, executionOrder);

        foreach (var entity in cyclicEntities)
        {
            entity.Executed = false;
            entity.ExecutionError = "存在循环依赖，无法调度（Kahn 拓扑排序留剩）";
            result.AddFailure(
                entity.Name,
                entity.FuncRef ?? string.Empty,
                entity.ExecutionError,
                isDependencyError: false);
            failedEntities.Add(entity.Uid);
        }

        foreach (var entity in executionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var predecessors = graph.GetPredecessors(entity.Uid).ToList();
            var failedPredecessor = predecessors.FirstOrDefault(p => failedEntities.Contains(p.Uid));
            if (failedPredecessor != null)
            {
                entity.Executed = false;
                entity.ExecutionError = $"依赖的前驱 '{failedPredecessor.Name}' 失败，跳过执行";
                result.AddFailure(
                    entity.Name,
                    entity.FuncRef ?? string.Empty,
                    entity.ExecutionError,
                    isDependencyError: true);
                continue;
            }

            if (BeforeEntityExecuted != null)
                await BeforeEntityExecuted(cancellationToken);

            var entityResult = await ExecuteEntityAsync(graph, entity, cancellationToken);

            if (!entityResult.IsSuccess)
            {
                entity.Executed = false;
                entity.ExecutionError = entityResult.ErrorMessage ?? "执行失败";
                result.AddFailure(entity.Name, entity.FuncRef ?? string.Empty, entity.ExecutionError);
                // 失败不中断：记录失败但不加入 failedEntities，避免后继步骤因 failedPredecessor 被跳过。
                if (!ContinueOnFailure)
                    failedEntities.Add(entity.Uid);
                continue;
            }

            entity.Executed = true;

            ExposeOutput(result, entity);
        }

        ApplyOutputMapping(graph, result);

        result.Value = result.Outputs.Count > 0
            ? result.Outputs.LastOrDefault().Value
            : graph.Entities.Values.LastOrDefault(e => e.Executed)?.Value;

        return result;
    }

    private async Task<IntentResult> ExecuteEntityAsync(
        ExecutionGraph graph,
        RgeEntity entity,
        CancellationToken cancellationToken)
        => await _entityHelper.ExecuteEntityAsync(entity, graph, cancellationToken);

    private static void ExposeOutput(GraphExecutionResult result, RgeEntity entity)
    {
        if (entity.Outputs.Count > 0)
        {
            foreach (var kvp in entity.Outputs)
                result.Outputs[$"{entity.Uid}/{kvp.Key}"] = kvp.Value;
        }

        if (string.IsNullOrWhiteSpace(entity.OutputKey) && entity.Value != null)
            result.Outputs[entity.Uid] = entity.Value;
    }

    private static void ApplyOutputMapping(ExecutionGraph graph, GraphExecutionResult result)
    {
        foreach (var mapping in graph.OutputMapping)
        {
            var entityKey = mapping.Key;

            if (result.Outputs.TryGetValue(entityKey, out var exactValue))
            {
                result.Outputs[mapping.Value] = exactValue;
                continue;
            }

            var match = result.Outputs
                .FirstOrDefault(kvp => kvp.Key.StartsWith(entityKey + "/", StringComparison.OrdinalIgnoreCase));
            if (match.Key != null)
                result.Outputs[mapping.Value] = match.Value;
        }
    }

}

public static class RgeGraphSchedulerExtensions
{
    public static RgeGraphScheduler CreateDefault(TConfig.IFunctionExecutor? functionExecutor = null)
    {
        var router = IntentSystemRouter.CreateDefault(functionExecutor);
        return new RgeGraphScheduler(router: router);
    }
}