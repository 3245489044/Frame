
using Container.Abstractions;
using Polly;
using Polly.Timeout;

namespace Container.Decorators;

public class TimeoutDecorator : IEventFunctionExecutor
{
    private readonly IEventFunctionExecutor _inner;
    private readonly IAsyncPolicy<FunctionExecutionResult> _timeoutPolicy;

    public TimeoutDecorator(
        IEventFunctionExecutor inner,
        int timeoutSeconds = PolicyFactory.DefaultTimeoutSeconds)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timeoutPolicy = PolicyFactory.CreateTimeoutPolicy<FunctionExecutionResult>(timeoutSeconds);
    }

    public async Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _timeoutPolicy.ExecuteAsync(
                ct => _inner.ExecuteFunctionAsync(functionName, parameters, ct),
                cancellationToken);
        }
        catch (TimeoutRejectedException)
        {
            return FunctionExecutionResult.Fail(
                $"函数 '{functionName}' 执行超时");
        }
    }
}
