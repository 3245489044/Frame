using ExecutionFramework.Abstractions;
using ExecutionFramework.Reference;
using ExecutionFramework.Systems;
using Rge.Unified.Systems;
using TConfig;

namespace ExecutionFramework.Engine;

public sealed class RgeExecutionOrchestrator : IExecutionOrchestrator
{
    private readonly RgeGraphScheduler _scheduler;
    private readonly IExecutionLogger? _logger;

    public RgeExecutionOrchestrator(
        RgeGraphScheduler? scheduler = null,
        IExecutionLogger? logger = null)
    {
        _scheduler = scheduler ?? RgeGraphSchedulerExtensions.CreateDefault();
        _logger = logger;
    }

    public async Task<TConfig.ExecutionResult> OrchestrateAsync(
        ExecutionPlan plan,
        IExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var graph = ExecutionGraphAdapter.FromPlan(plan, rootInput: BuildRootInput(context));

        _scheduler.BeforeEntityExecuted = ct => ExecutionPauseCoordinator.CheckPauseAsync(context, ct);

        try
        {
            var graphResult = await _scheduler.ExecuteAsync(graph, cancellationToken);

            if (!graphResult.IsSuccess)
            {
                return TConfig.ExecutionResult.Failure(BuildErrorMessage(graphResult));
            }

            return AdaptSuccessResult(plan, graphResult);
        }
        finally
        {
            _scheduler.BeforeEntityExecuted = null;
        }
    }


    private static object? BuildRootInput(IExecutionContext context)
    {
        if (context is TConfig.ExecutionContext typed)
            return typed.Parameters;

        if (context.Items.TryGetValue(ExecutionGraphAdapter.RootInputMetadataKey, out var ri))
            return ri;

        return null;
    }

    private const int MaxSingleValueUnwrapDepth = 4;

    private static TConfig.ExecutionResult AdaptSuccessResult(ExecutionPlan plan, GraphExecutionResult graphResult)
    {
        if (plan.ResultMode == ResultCollectionMode.Single)
        {
            return TConfig.ExecutionResult.Success(UnwrapSingleValue(graphResult.Value, 0));
        }

        if (graphResult.Outputs.Count > 0)
        {
            return TConfig.ExecutionResult.Success(new Dictionary<string, object?>(graphResult.Outputs));
        }

        return TConfig.ExecutionResult.Success(graphResult.Value);
    }

    /// <summary>
    /// Single 模式语义 = 最后一个有效节点的业务值。Condition 实体的输出含
    /// <c>branchResults</c>（<see cref="IntentResult"/> 集合），若原样返回，消费方
    /// <c>ToString()</c> 会得到类型名（如 <c>System.Collections.Generic.List`1[...]</c>）。
    /// 逐层解包（深度上限防环）直到非 IntentResult / 非 IntentResult 集合。
    /// </summary>
    private static object? UnwrapSingleValue(object? value, int depth)
    {
        if (value is null || depth >= MaxSingleValueUnwrapDepth)
            return value;

        if (value is IntentResult intent)
            return UnwrapSingleValue(intent.Value, depth + 1);

        if (value is IEnumerable<IntentResult> results)
        {
            var last = results.LastOrDefault(r => r.IsSuccess && r.Value is not null);
            return last is null ? value : UnwrapSingleValue(last.Value, depth + 1);
        }

        return value;
    }

    private static string BuildErrorMessage(GraphExecutionResult graphResult)
        => graphResult.BuildErrorMessage();
}