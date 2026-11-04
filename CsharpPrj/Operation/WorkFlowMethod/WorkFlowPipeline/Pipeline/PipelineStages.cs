using BasicExecutionResult = TConfig.ExecutionResult;
using ExecutionFramework.Abstractions;

namespace WorkflowExecution.Pipeline;

using Abstractions;

public class StageRunner : IPipelineStage
{
    public string Name { get; }
    public float Order { get; }
    private readonly Func<TConfig.IExecutionContext, CancellationToken, Task<BasicExecutionResult>> _executeFunc;

    public StageRunner(
        string name,
        float order,
        Func<TConfig.IExecutionContext, CancellationToken, Task<BasicExecutionResult>> executeFunc)
    {
        Name = name;
        Order = order;
        _executeFunc = executeFunc;
    }

    public Task<BasicExecutionResult> ExecuteAsync(TConfig.IExecutionContext context, CancellationToken cancellationToken)
        => _executeFunc(context, cancellationToken);
}
