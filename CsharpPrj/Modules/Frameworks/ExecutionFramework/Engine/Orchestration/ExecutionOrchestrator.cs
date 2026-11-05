namespace ExecutionFramework.Engine;

using TConfig;

public interface IExecutionOrchestrator
{
    Task<TConfig.ExecutionResult> OrchestrateAsync(
        ExecutionPlan plan,
        TConfig.IExecutionContext context,
        CancellationToken cancellationToken = default);

}
