
using CommandFramework.Abstractions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using BP = Modules.Pipeline;

namespace CommandFramework.Pipeline.Stages;

public enum RetryDelayStrategy
{
    Fixed,

    ExponentialBackoff,

    Linear
}

public class RetryStage<TContext> : IPipelineStage<TContext>
    where TContext : BP.IPipelineContext
{
    private readonly IPipelineStage<TContext> _innerStage;
    private readonly int _maxRetries;
    private readonly int _baseDelayMs;
    private readonly RetryDelayStrategy _delayStrategy;
    private readonly Func<TContext, bool>? _shouldRetry;
    private readonly ILogger? _logger;

    private ResiliencePipeline? _retryPipeline;
    private readonly object _pipelineLock = new();

    public string Name => $"Retry({_innerStage.Name})";

    public string Description => $"Retry wrapper for {_innerStage.Name} (max {_maxRetries} times)";

    public RetryStage(
        IPipelineStage<TContext> innerStage,
        int maxRetries = 3,
        int baseDelayMs = 100,
        RetryDelayStrategy delayStrategy = RetryDelayStrategy.Fixed,
        Func<TContext, bool>? shouldRetry = null,
        ILogger? logger = null)
    {
        _innerStage = innerStage ?? throw new ArgumentNullException(nameof(innerStage));
        _maxRetries = Math.Max(1, maxRetries);
        _baseDelayMs = Math.Max(0, baseDelayMs);
        _delayStrategy = delayStrategy;
        _shouldRetry = shouldRetry;
        _logger = logger;
    }

    public bool CanExecute(TContext context) => _innerStage.CanExecute(context);

    public async Task ExecuteAsync(TContext context, CancellationToken cancellationToken = default)
    {
        var pipeline = GetOrCreatePipeline();

        await pipeline.ExecuteAsync(
            async ct =>
            {
                await _innerStage.ExecuteAsync(context, ct);

                if (!context.GetStageSuccess(_innerStage.Name))
                {
                    if (_shouldRetry != null && !_shouldRetry(context))
                        return;

                    context.SetStageResult(_innerStage.Name, true, null);
                    throw new InvalidOperationException(
                        $"Stage '{_innerStage.Name}' failed, retrying...");
                }
            },
            cancellationToken);
    }

    private ResiliencePipeline GetOrCreatePipeline()
    {
        if (_retryPipeline != null)
            return _retryPipeline;

        lock (_pipelineLock)
        {
            if (_retryPipeline != null)
                return _retryPipeline;

            var retryOptions = new RetryStrategyOptions
            {
                MaxRetryAttempts = _maxRetries,
                Delay = TimeSpan.FromMilliseconds(_baseDelayMs),
                DelayGenerator = CreateDelayGenerator(),
                OnRetry = args =>
                {
                    _logger?.LogWarning(
                        "Retry {Attempt}/{MaxRetries} for stage '{StageName}' after {DelayMs:F0}ms. Error: {ErrorMessage}",
                        args.AttemptNumber + 1,
                        _maxRetries,
                        _innerStage.Name,
                        args.Duration.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return default;
                }
            };

            _retryPipeline = new ResiliencePipelineBuilder()
                .AddRetry(retryOptions)
                .Build();

            return _retryPipeline;
        }
    }

    private Func<RetryDelayGeneratorArguments<object>, ValueTask<TimeSpan?>>? CreateDelayGenerator()
    {
        return _delayStrategy switch
        {
            RetryDelayStrategy.ExponentialBackoff => args =>
            {
                var delayMs = Math.Min(
                    (long)_baseDelayMs * (1L << Math.Min(args.AttemptNumber, 30)),
                    3_600_000L);
                return ValueTask.FromResult<TimeSpan?>(
                    TimeSpan.FromMilliseconds(delayMs));
            },

            RetryDelayStrategy.Linear => args =>
                ValueTask.FromResult<TimeSpan?>(
                    TimeSpan.FromMilliseconds(_baseDelayMs * (args.AttemptNumber + 1L))),

            _ => null
        };
    }
}

