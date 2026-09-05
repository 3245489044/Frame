
using Container.Abstractions;
using Microsoft.Extensions.Logging;

namespace Container.Decorators;

public static class EventFunctionExecutorFactory
{
    public static IEventFunctionExecutor BuildChain(
        IEventFunctionExecutor inner,
        FunctionExecutorOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        options ??= new FunctionExecutorOptions();
        IEventFunctionExecutor current = inner;


        if (options.EnableCircuitBreaker)
        {
            current = new CircuitBreakerDecorator(
                current,
                options.CircuitBreakerThreshold,
                options.CircuitBreakerDurationSeconds);
        }

        if (options.EnableRetry)
        {
            current = new RetryDecorator(
                current,
                options.MaxRetries,
                options.RetryDelayMs);
        }

        if (options.EnableTimeout)
        {
            var timeoutSeconds = options.IsEventMode
                ? options.EventTimeoutSeconds
                : options.TimeoutSeconds;
            current = new TimeoutDecorator(current, timeoutSeconds);
        }

        if (options.EnableLogging && loggerFactory != null)
        {
            var logger = loggerFactory.CreateLogger<LoggingDecorator>();
            current = new LoggingDecorator(current, logger);
        }

        return current;
    }

    public static IEventFunctionExecutor CreateForEventMode(
        IEventFunctionExecutor inner,
        ILoggerFactory? loggerFactory = null)
    {
        var options = new FunctionExecutorOptions
        {
            IsEventMode = true,
            EnableCircuitBreaker = false,
            EnableRetry = true,
            MaxRetries = 2,
            RetryDelayMs = 50,
            EventTimeoutSeconds = 10
        };

        return BuildChain(inner, options, loggerFactory);
    }

    public static IEventFunctionExecutor CreateForScriptMode(
        IEventFunctionExecutor inner,
        ILoggerFactory? loggerFactory = null)
    {
        var options = new FunctionExecutorOptions
        {
            IsEventMode = false,
            EnableCircuitBreaker = true,
            EnableRetry = true,
            MaxRetries = 3,
            RetryDelayMs = 100,
            TimeoutSeconds = 30
        };

        return BuildChain(inner, options, loggerFactory);
    }
}
