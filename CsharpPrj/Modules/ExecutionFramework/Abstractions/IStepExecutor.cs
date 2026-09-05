using TConfig;

namespace ExecutionFramework.Abstractions;

public interface IStepExecutor
{
    Task<TConfig.ExecutionResult> ExecuteStepAsync(
        TConfig.StepDefinition step,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default);

    bool CanExecute(TConfig.StepDefinition step, TConfig.IExecutionContext context);
}
