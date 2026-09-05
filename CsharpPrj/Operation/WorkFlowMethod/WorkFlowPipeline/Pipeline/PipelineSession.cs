
using System.Diagnostics;
using BasicExecutionResult = TConfig.ExecutionResult;
using ExecutionFramework.Abstractions;
using Modules.StateMachine;
using WorkflowExecution.Abstractions;
using WorkFlowMethod.Diagnostics;

namespace WorkflowExecution.Pipeline;

public class PipelineSession : IDisposable
{
    private readonly IReadOnlyList<IPipelineStage> _stages;
    private bool _disposed;

    public ConditionalStateMachine<Modules.StateMachine.ExecutionStatus> StateMachine { get; } = new(Modules.StateMachine.ExecutionStatus.Pending);

    public PipelineSession(
        IReadOnlyList<IPipelineStage> stages)
    {
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
    }

    public async Task<BasicExecutionResult> ExecuteAsync(
        TConfig.ExecutionPlan workflow,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(context);

        if (!StateMachine.TryTransitionFrom(Modules.StateMachine.ExecutionStatus.Pending, Modules.StateMachine.ExecutionStatus.Running))
        {
            return BasicExecutionResult.Failure("Pipeline session is already running or has completed");
        }

        WorkFlowMethodLog.Log("PipelineSession_Execute_start",
            new { WorkflowName = workflow.Name, StepCount = workflow.Steps.Count });

        var stopwatch = Stopwatch.StartNew();
        object? finalValue = null;
        var stageResults = new Dictionary<string, object?>();

        try
        {
            foreach (var stage in _stages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                WorkFlowMethodLog.Log("PipelineSession_Stage_start",
                    new { StageName = stage.Name });

                var stageSw = Stopwatch.StartNew();
                var result = await stage.ExecuteAsync(context, cancellationToken);
                stageSw.Stop();

                if (!result.IsSuccess)
                {
                    stopwatch.Stop();
                    WorkFlowMethodLog.Log("PipelineSession_Stage_complete",
                        new { StageName = stage.Name, Success = false, ElapsedMs = stageSw.ElapsedMilliseconds });
                    WorkFlowMethodLog.Log("PipelineSession_Stage_failed",
                        new { stageName = stage.Name, error = result.ErrorMessage });
                    if (!StateMachine.TryTransitionFrom(Modules.StateMachine.ExecutionStatus.Running, Modules.StateMachine.ExecutionStatus.Failed))
                    {
                        WorkFlowMethodLog.Log("PipelineSession_StateTransitionFailed",
                            new { from = "Running", to = "Failed" });
                    }
                    return result;
                }
                WorkFlowMethodLog.Log("PipelineSession_Stage_complete",
                    new { StageName = stage.Name, Success = true, ElapsedMs = stageSw.ElapsedMilliseconds });
                finalValue = result.Value;
                context.Items[$"stage.{stage.Name}.result"] = result.Value;
                stageResults[stage.Name] = result.Value;
            }

            context.Items["stage.results"] = stageResults;

            stopwatch.Stop();
            WorkFlowMethodLog.Log("PipelineSession_Execute_complete",
                new { WorkflowName = workflow.Name, Success = true, ElapsedMs = stopwatch.ElapsedMilliseconds });
            if (!StateMachine.TryTransitionFrom(Modules.StateMachine.ExecutionStatus.Running, Modules.StateMachine.ExecutionStatus.Completed))
            {
                WorkFlowMethodLog.Log("PipelineSession_StateTransitionFailed",
                    new { from = "Running", to = "Completed" });
            }
            return BasicExecutionResult.Success(finalValue, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            if (!StateMachine.TryTransitionFrom(Modules.StateMachine.ExecutionStatus.Running, Modules.StateMachine.ExecutionStatus.Cancelled))
            {
                WorkFlowMethodLog.Log("PipelineSession_StateTransitionFailed",
                    new { from = "Running", to = "Cancelled" });
            }
            return BasicExecutionResult.Cancelled();
        }
        catch (Exception ex)
        {
            if (!StateMachine.TryTransitionFrom(Modules.StateMachine.ExecutionStatus.Running, Modules.StateMachine.ExecutionStatus.Failed))
            {
                WorkFlowMethodLog.Log("PipelineSession_StateTransitionFailed",
                    new { from = "Running", to = "Failed" });
            }
            WorkFlowMethodLog.Log("PipelineSession_Execute_exception", new { error = ex.Message });
            return BasicExecutionResult.Failure(ex.Message, ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StateMachine.Reset(Modules.StateMachine.ExecutionStatus.Pending);
    }
}
