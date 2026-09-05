
namespace Container.Abstractions;

public interface IEventFunctionExecutor
{
    Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken = default);
}

public record FunctionExecutionResult(
    bool Success,
    object? Value,
    string? Error,
    TimeSpan ExecutionTime
)
{
    public static FunctionExecutionResult Ok(object? value = null, TimeSpan? executionTime = null)
        => new(true, value, null, executionTime ?? TimeSpan.Zero);

    public static FunctionExecutionResult Fail(string error, TimeSpan? executionTime = null)
        => new(false, null, error, executionTime ?? TimeSpan.Zero);
}

public class FunctionExecutorOptions
{
    public bool EnableRetry { get; set; } = true;

    public bool EnableCircuitBreaker { get; set; } = false;

    public bool EnableTimeout { get; set; } = true;

    public bool EnableLogging { get; set; } = true;

    public int MaxRetries { get; set; } = 3;

    public int RetryDelayMs { get; set; } = 100;

    public int TimeoutSeconds { get; set; } = 30;

    public int CircuitBreakerThreshold { get; set; } = 5;

    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    public bool IsEventMode { get; set; } = false;

    public int EventTimeoutSeconds { get; set; } = 10;
}
