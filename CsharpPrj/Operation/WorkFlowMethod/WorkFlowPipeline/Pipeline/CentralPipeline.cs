using System.Diagnostics;
using BasicExecutionResult = TConfig.ExecutionResult;
using CommandFramework.Execution;
using ExecutionFramework.Abstractions;
using ExecutionFramework.Engine;
using WorkFlowMethod.Diagnostics;

namespace WorkflowExecution.Pipeline;

using Abstractions;
using Engine;

public class CentralPipeline : PipelineBase<TConfig.IExecutionContext>
{
    private readonly IStepExecutor _stepExecutor;

    public CentralPipeline(
        IStepExecutor stepExecutor,
        IFunctionExecutor atomExecutor,
        IEnumerable<IPipelineStage>? additionalStages = null)
        : base("CentralPipeline", BuildFixedStages(stepExecutor, additionalStages, CreateOrchestrator(atomExecutor)))
    {
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
    }

    private static List<IPipelineStage> BuildFixedStages(
        IStepExecutor stepExecutor,
        IEnumerable<IPipelineStage>? additionalStages,
        RgeExecutionOrchestrator orchestrator)
    {
        var stages = new List<IPipelineStage>
        {
            new StageRunner(PipelineStageNames.Initialization, 0, ExecuteInitializationAsync),
            new StageRunner(PipelineStageNames.Execution, 2,
                (ctx, ct) => ExecuteExecutionAsync(ctx, ct, orchestrator)),
            new StageRunner(PipelineStageNames.Finalization, 4, ExecuteFinalizationAsync),
        };

        if (additionalStages != null)
        {
            stages.AddRange(additionalStages);
        }

        return stages;
    }

    private static RgeExecutionOrchestrator CreateOrchestrator(IFunctionExecutor atomExecutor)
        => new RgeExecutionOrchestrator(RgeGraphSchedulerExtensions.CreateDefault(atomExecutor));

    public PipelineSession CreateSession()
    {
        var stages = GetResolvedStages();
        return new PipelineSession(stages);
    }


    protected static Task<BasicExecutionResult> ExecuteInitializationAsync(
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(BasicExecutionResult.Success(null));
    }

    protected static Task<BasicExecutionResult> ExecuteFinalizationAsync(
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(BasicExecutionResult.Success(null));
    }

    private static async Task<BasicExecutionResult> ExecuteExecutionAsync(
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken,
        RgeExecutionOrchestrator orchestrator)
    {
        if (!context.Items.TryGetValue("ExecutionPlan", out var planObj) || planObj is not TConfig.ExecutionPlan plan)
        {
            return BasicExecutionResult.Success(null);
        }

        if (plan.Steps.Count == 0)
        {
            return BasicExecutionResult.Success(null);
        }

        WorkFlowMethodLog.Log("CentralPipeline_Execution_start",
            new { StepCount = plan.Steps.Count });

        var result = await orchestrator.OrchestrateAsync(plan, context, cancellationToken);

        if (!result.IsSuccess)
        {
            WorkFlowMethodLog.Log("CentralPipeline_Execution_error",
                new { Error = result.ErrorMessage });
        }

        return result;
    }
}