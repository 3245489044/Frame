
#nullable enable

using ECS.Core;
using GraphFramework.Diagnostics;

namespace GraphFramework.Pipeline
{
    public interface IPipelineStepDecorator
    {
        string Name { get; }
    }

    public interface IPipelineStepDecorator<TContext> : IPipelineStepDecorator
    {
        void BeforeExecute(TContext context);

        void AfterExecute(TContext context, Exception? exception);
    }

    public class LogDecorator<TContext> : IPipelineStepDecorator<TContext>
    {
        public string Name => "Log";

        private readonly string _stepName;

        public LogDecorator(string stepName = "")
        {
            _stepName = stepName;
        }

        public void BeforeExecute(TContext context)
        {
            var name = string.IsNullOrEmpty(_stepName) ? "Step" : _stepName;
            GraphFrameworkDiagnosticContext.Record(
                "pipeline.step.executing", new { stepName = name });
        }

        public void AfterExecute(TContext context, Exception? exception)
        {
            var name = string.IsNullOrEmpty(_stepName) ? "Step" : _stepName;
            if (exception != null)
            {
                GraphFrameworkDiagnosticContext.Record(
                    "pipeline.step.failed", new { stepName = name, error = exception.Message });
            }
            else
            {
                GraphFrameworkDiagnosticContext.Record(
                    "pipeline.step.completed", new { stepName = name });
            }
        }
    }

    public class TimingDecorator<TContext> : IPipelineStepDecorator<TContext>
    {
        public string Name => "Timing";

        private readonly string _stepName;
        private System.Diagnostics.Stopwatch? _stopwatch;

        public TimingDecorator(string stepName = "")
        {
            _stepName = stepName;
        }

        public void BeforeExecute(TContext context)
        {
            _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        }

        public void AfterExecute(TContext context, Exception? exception)
        {
            _stopwatch?.Stop();
            var name = string.IsNullOrEmpty(_stepName) ? "Step" : _stepName;
            GraphFrameworkDiagnosticContext.Record(
                "pipeline.step.timing", new { stepName = name, elapsedMs = _stopwatch?.ElapsedMilliseconds ?? 0 });
        }
    }

    public class TimeoutDecorator<TContext> : IPipelineStepDecorator<TContext>
    {
        public string Name => "Timeout";

        private readonly TimeSpan _timeout;
        private readonly string _stepName;
        private CancellationTokenSource? _cts;

        public TimeSpan Timeout => _timeout;

        public CancellationTokenSource? CancellationTokenSource => _cts;

        public TimeoutDecorator(TimeSpan timeout, string stepName = "")
        {
            _timeout = timeout;
            _stepName = stepName;
        }

        public void BeforeExecute(TContext context)
        {
            _cts = new CancellationTokenSource(_timeout);
        }

        public void AfterExecute(TContext context, Exception? exception)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public class RetryDecorator<TContext> : IPipelineStepDecorator<TContext>
    {
        public string Name => "Retry";

        private readonly int _maxRetries;
        private readonly TimeSpan _retryDelay;
        private readonly string _stepName;
        private int _attemptCount;

        public int AttemptCount => _attemptCount;

        public int MaxRetries => _maxRetries;

        public TimeSpan RetryDelay => _retryDelay;

        public bool CanRetry => _attemptCount < _maxRetries;

        public RetryDecorator(int maxRetries = 3, TimeSpan? retryDelay = null, string stepName = "")
        {
            _maxRetries = maxRetries;
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100);
            _stepName = stepName;
        }

        public void BeforeExecute(TContext context)
        {
            _attemptCount++;
        }

        public void AfterExecute(TContext context, Exception? exception)
        {
            if (exception != null && _attemptCount < _maxRetries)
            {
                var name = string.IsNullOrEmpty(_stepName) ? "Step" : _stepName;
                GraphFrameworkDiagnosticContext.Record(
                    "pipeline.step.retry", new { stepName = name, attempt = _attemptCount, maxRetries = _maxRetries, retryDelayMs = _retryDelay.TotalMilliseconds });
                Thread.Sleep(_retryDelay);
            }
        }
    }

    public class DecoratorChain<TContext>
    {
        private readonly List<IPipelineStepDecorator<TContext>> _decorators = new();
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _decorators.Count; }
        }

        public DecoratorChain<TContext> Add(IPipelineStepDecorator<TContext> decorator)
        {
            decorator = PortGuard.Required(decorator, nameof(decorator));

            lock (_lock)
            {
                _decorators.Add(decorator);
            }

            return this;
        }

        public DecoratorChain<TContext> AddRange(IEnumerable<IPipelineStepDecorator<TContext>> decorators)
        {
            decorators = PortGuard.Required(decorators, nameof(decorators));

            lock (_lock)
            {
                _decorators.AddRange(decorators);
            }

            return this;
        }

        public bool Remove(IPipelineStepDecorator<TContext> decorator)
        {
            lock (_lock)
            {
                return _decorators.Remove(decorator);
            }
        }

        public int RemoveAll<T>() where T : IPipelineStepDecorator<TContext>
        {
            lock (_lock)
            {
                return _decorators.RemoveAll(d => d is T);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _decorators.Clear();
            }
        }

        private List<IPipelineStepDecorator<TContext>> GetSnapshot()
        {
            lock (_lock)
            {
                return _decorators.ToList();
            }
        }

        public IReadOnlyList<IPipelineStepDecorator<TContext>> GetDecorators()
            => GetSnapshot();

        internal void BeforeExecute(TContext context)
        {
            foreach (var decorator in GetSnapshot())
            {
                decorator.BeforeExecute(context);
            }
        }

        internal void AfterExecute(TContext context, Exception? exception)
        {
            var snapshot = GetSnapshot();

            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                snapshot[i].AfterExecute(context, exception);
            }
        }

        public static DecoratorChain<TContext> CreateDefault(string stepName = "")
            => new DecoratorChain<TContext>()
                .Add(new LogDecorator<TContext>(stepName))
                .Add(new TimingDecorator<TContext>(stepName));

        public static DecoratorChain<TContext> CreateProduction(string stepName = "",
            TimeSpan? timeout = null, int maxRetries = 3)
            => new DecoratorChain<TContext>()
                .Add(new LogDecorator<TContext>(stepName))
                .Add(new TimingDecorator<TContext>(stepName))
                .Add(new TimeoutDecorator<TContext>(timeout ?? TimeSpan.FromSeconds(30), stepName))
                .Add(new RetryDecorator<TContext>(maxRetries, stepName: stepName));
    }
 
}
