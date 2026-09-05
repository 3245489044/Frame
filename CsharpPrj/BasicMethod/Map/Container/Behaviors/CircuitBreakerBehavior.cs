
using Container.Abstractions;
using MediatR;
using Polly;

namespace Container.Behaviors;

public class CircuitBreakerBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ExecutionRequest
    where TResponse : ExecutionResult
{
    private readonly IAsyncPolicy<TResponse> _circuitBreaker;

    public CircuitBreakerBehavior(
        int exceptionsAllowedBeforeBreaking = PolicyFactory.DefaultCircuitBreakerThreshold,
        int durationOfBreakSeconds = PolicyFactory.DefaultCircuitBreakerDurationSeconds)
    {
        _circuitBreaker = PolicyFactory.CreateCircuitBreakerPolicy<TResponse>(
            exceptionsAllowedBeforeBreaking, durationOfBreakSeconds);
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return await _circuitBreaker.ExecuteAsync(ct => next(), cancellationToken);
    }
}
