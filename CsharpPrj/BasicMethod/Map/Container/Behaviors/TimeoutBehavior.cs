
using Container.Abstractions;
using MediatR;
using Polly;
using Polly.Timeout;

namespace Container.Behaviors;

public class TimeoutBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
    where TResponse : ExecutionResult
{
    private readonly IAsyncPolicy<TResponse> _timeoutPolicy;

    public TimeoutBehavior(int timeoutSeconds = PolicyFactory.DefaultTimeoutSeconds)
    {
        _timeoutPolicy = PolicyFactory.CreateTimeoutPolicy<TResponse>(timeoutSeconds);
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return await _timeoutPolicy.ExecuteAsync(ct => next(), cancellationToken);
    }
}
