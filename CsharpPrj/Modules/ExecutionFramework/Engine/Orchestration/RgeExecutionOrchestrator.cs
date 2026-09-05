using ExecutionFramework.Abstractions;
using ExecutionFramework.Reference;
using ExecutionFramework.Systems;
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

    private static TConfig.ExecutionResult AdaptSuccessResult(ExecutionPlan plan, GraphExecutionResult graphResult)
    {
        if (plan.ResultMode == ResultCollectionMode.Single)
        {
            return TConfig.ExecutionResult.Success(graphResult.Value);
        }

        if (graphResult.Outputs.Count > 0)
        {
            return TConfig.ExecutionResult.Success(new Dictionary<string, object?>(graphResult.Outputs));
        }

        return TConfig.ExecutionResult.Success(graphResult.Value);
    }

    private static string BuildErrorMessage(GraphExecutionResult graphResult)
        => graphResult.BuildErrorMessage();
}