
using Container.Abstractions;
using Polly;

namespace Container.Decorators;

public class RetryDecorator : IEventFunctionExecutor
{
    private readonly IEventFunctionExecutor _inner;
    private readonly IAsyncPolicy<FunctionExecutionResult> _retryPolicy;

    public RetryDecorator(
        IEventFunctionExecutor inner,
        int maxRetries = PolicyFactory.DefaultMaxRetries,
        int delayMs = PolicyFactory.DefaultRetryDelayMs)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _retryPolicy = PolicyFactory.CreateRetryPolicy<FunctionExecutionResult>(
            maxRetries, delayMs, r => !r.Success);
    }

    public async Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(
            ct => _inner.ExecuteFunctionAsync(functionName, parameters, ct),
            cancellationToken);
    }
}
