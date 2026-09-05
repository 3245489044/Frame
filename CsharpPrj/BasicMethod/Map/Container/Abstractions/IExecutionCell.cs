
namespace Container.Abstractions;

public interface IExecutionCell
{
    string Name { get; }

    Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default);
}
