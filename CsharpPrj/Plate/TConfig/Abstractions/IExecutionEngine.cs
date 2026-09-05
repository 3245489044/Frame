
namespace TConfig;

public interface IExecutionEngine
{
    Task<ExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default);

    Task CancelAsync(string planId);

    Task PauseAsync(string planId);

    Task ResumeAsync(string planId);

    ExecutionStatus GetStatus(string planId);

    async Task<ExecutionResult> ExecuteFunctionAsync(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var plan = ExecutionPlan.FromFunction(functionName, parameters);
        context ??= new ExecutionContext();
        return await ExecuteAsync(plan, context, cancellationToken);
    }

    async Task<ExecutionResult> ExecuteWorkflowAsync(
        string workflowName,
        List<StepDefinition> steps,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var plan = ExecutionPlan.FromWorkflowNodes(workflowName, steps);
        context ??= new ExecutionContext();
        return await ExecuteAsync(plan, context, cancellationToken);
    }
}
