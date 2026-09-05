
using Container.Abstractions;
using Polly;
using Polly.CircuitBreaker;

namespace Container.Decorators;

public class CircuitBreakerDecorator : IEventFunctionExecutor
{
    private readonly IEventFunctionExecutor _inner;
    private readonly IAsyncPolicy<FunctionExecutionResult> _circuitBreakerPolicy;

    public CircuitBreakerDecorator(
        IEventFunctionExecutor inner,
        int threshold = PolicyFactory.DefaultCircuitBreakerThreshold,
        int durationSeconds = PolicyFactory.DefaultCircuitBreakerDurationSeconds)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _circuitBreakerPolicy = PolicyFactory.CreateCircuitBreakerPolicy<FunctionExecutionResult>(
            threshold, durationSeconds, r => !r.Success);
    }

    public async Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _circuitBreakerPolicy.ExecuteAsync(
                ct => _inner.ExecuteFunctionAsync(functionName, parameters, ct),
                cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            return FunctionExecutionResult.Fail(
                $"熔断器已打开，函数 '{functionName}' 暂时无法执行");
        }
    }
}
