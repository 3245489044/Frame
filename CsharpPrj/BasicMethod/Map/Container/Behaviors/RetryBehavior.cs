
using Container.Abstractions;
using MediatR;
using Polly;

namespace Container.Behaviors;

public class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
    where TResponse : ExecutionResult
{
    private readonly IAsyncPolicy<TResponse> _retryPolicy;

    public RetryBehavior(int maxRetries = PolicyFactory.DefaultMaxRetries, int delayMs = PolicyFactory.DefaultRetryDelayMs)
    {
        _retryPolicy = PolicyFactory.CreateRetryPolicy<TResponse>(maxRetries, delayMs);
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync(ct => next(), cancellationToken);
    }
}
