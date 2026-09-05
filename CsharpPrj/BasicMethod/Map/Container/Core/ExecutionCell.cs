
using Container.Abstractions;
using MediatR;

namespace Container.Core;

public class ExecutionCell : IExecutionCell
{
    private readonly IMediator _mediator;
    private readonly string _name;

    public string Name => _name;

    public ExecutionCell(IMediator mediator, string? name = null)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _name = name ?? "ExecutionCell";
    }

    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _mediator.Send(request, cancellationToken);
    }
}
