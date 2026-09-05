
using Polly;

namespace Container;

public static class PolicyFactory
{
    public const int DefaultMaxRetries = 3;
    public const int DefaultRetryDelayMs = 100;
    public const int DefaultTimeoutSeconds = 30;
    public const int DefaultCircuitBreakerThreshold = 5;
    public const int DefaultCircuitBreakerDurationSeconds = 30;

    public static IAsyncPolicy<TResult> CreateRetryPolicy<TResult>(
        int maxRetries = DefaultMaxRetries,
        int delayMs = DefaultRetryDelayMs,
        Func<TResult, bool>? resultPredicate = null)
    {
        var policy = Policy<TResult>.Handle<Exception>();
        if (resultPredicate != null)
            policy = policy.OrResult(resultPredicate);
        return policy.WaitAndRetryAsync(
            maxRetries,
            retryAttempt => TimeSpan.FromMilliseconds(delayMs * retryAttempt));
    }

    public static IAsyncPolicy<TResult> CreateTimeoutPolicy<TResult>(
        int timeoutSeconds = DefaultTimeoutSeconds)
    {
        return Policy.TimeoutAsync<TResult>(timeoutSeconds);
    }

    public static IAsyncPolicy<TResult> CreateCircuitBreakerPolicy<TResult>(
        int threshold = DefaultCircuitBreakerThreshold,
        int durationSeconds = DefaultCircuitBreakerDurationSeconds,
        Func<TResult, bool>? resultPredicate = null)
    {
        var policy = Policy<TResult>.Handle<Exception>();
        if (resultPredicate != null)
            policy = policy.OrResult(resultPredicate);
        return policy.CircuitBreakerAsync(
            threshold,
            TimeSpan.FromSeconds(durationSeconds));
    }
}
