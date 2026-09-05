
using Container.Abstractions;

namespace Container.Core;

public class BackgroundExecutionCell : IExecutionCell
{
    private readonly IExecutionCell _inner;

    public string Name => $"Background-{_inner.Name}";

    public BackgroundExecutionCell(IExecutionCell inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => _inner.ExecuteAsync(request, cancellationToken),
            cancellationToken);
    }
}
