using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Modules.Parsing.Abstractions;
using Modules.Parsing.Models;

namespace MetaCommand
{
    #region 统计实现

    public class CommandStatistics : ICommandStatistics
    {
        private readonly ConcurrentDictionary<string, VerbStatistics> _verbStats = new();
        private readonly ConcurrentQueue<ErrorRecord> _recentErrors = new();
        private int _totalExecutions;
        private int _successfulExecutions;
        private int _failedExecutions;
        private long _totalDurationMs;
        private readonly int _maxRecentErrors = 100;
        private DateTime _startTime;

        public void Start()
        {
            _startTime = DateTime.UtcNow;
        }

        public void Stop()
        {
        }

        public void RecordExecution(string verb, string? action, bool success, TimeSpan duration)
        {
            Interlocked.Increment(ref _totalExecutions);

            if (success)
                Interlocked.Increment(ref _successfulExecutions);
            else
                Interlocked.Increment(ref _failedExecutions);

            Interlocked.Add(ref _totalDurationMs, (long)duration.TotalMilliseconds);

            var safeVerb = verb ?? "unknown";
            var verbStats = _verbStats.GetOrAdd(safeVerb, _ => new VerbStatistics());
            verbStats.RecordExecution(action, success, duration);
        }

        public void RecordError(string verb, ErrorCode errorCode)
        {
            var errorRecord = new ErrorRecord
            {
                Verb = verb,
                ErrorCode = errorCode,
                Timestamp = DateTime.UtcNow
            };

            _recentErrors.Enqueue(errorRecord);

            while (_recentErrors.Count > _maxRecentErrors)
                _recentErrors.TryDequeue(out _);

            var verbStats = _verbStats.GetOrAdd(verb, _ => new VerbStatistics());
            verbStats.RecordError(errorCode);
        }

        public CommandMetrics GetMetrics()
        {
            var totalExecutions = Interlocked.CompareExchange(ref _totalExecutions, 0, 0);
            var successfulExecutions = Interlocked.CompareExchange(ref _successfulExecutions, 0, 0);
            var failedExecutions = Interlocked.CompareExchange(ref _failedExecutions, 0, 0);
            var totalDurationMs = Interlocked.CompareExchange(ref _totalDurationMs, 0, 0);

            return new CommandMetrics
            {
                TotalExecutions = totalExecutions,
                SuccessfulExecutions = successfulExecutions,
                FailedExecutions = failedExecutions,
                AverageDurationMs = totalExecutions > 0 ? (double)totalDurationMs / totalExecutions : 0,
                TotalDurationMs = totalDurationMs,
                VerbMetrics = _verbStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.GetMetrics()),
                RecentErrors = _recentErrors.ToList(),
                StartTime = _startTime,
                Uptime = _startTime != default ? DateTime.UtcNow - _startTime : TimeSpan.Zero
            };
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalExecutions, 0);
            Interlocked.Exchange(ref _successfulExecutions, 0);
            Interlocked.Exchange(ref _failedExecutions, 0);
            Interlocked.Exchange(ref _totalDurationMs, 0);
            _verbStats.Clear();
            while (_recentErrors.TryDequeue(out _)) { }
            _startTime = DateTime.UtcNow;
        }
    }


    #endregion

    #region 动词统计

    public class VerbStatistics
    {
        private int _executions;
        private int _successes;
        private int _failures;
        private long _totalDurationMs;
        private readonly ConcurrentDictionary<string, int> _actionCounts = new();
        private readonly ConcurrentDictionary<ErrorCode, int> _errorCounts = new();

        public void RecordExecution(string? action, bool success, TimeSpan duration)
        {
            Interlocked.Increment(ref _executions);

            if (success)
                Interlocked.Increment(ref _successes);
            else
                Interlocked.Increment(ref _failures);

            Interlocked.Add(ref _totalDurationMs, (long)duration.TotalMilliseconds);

            if (!string.IsNullOrEmpty(action))
                _actionCounts.AddOrUpdate(action, 1, (_, count) => count + 1);
        }

        public void RecordError(ErrorCode errorCode)
        {
            _errorCounts.AddOrUpdate(errorCode, 1, (_, count) => count + 1);
        }

        public VerbMetrics GetMetrics()
        {
            return new VerbMetrics
            {
                Executions = Interlocked.CompareExchange(ref _executions, 0, 0),
                Successes = Interlocked.CompareExchange(ref _successes, 0, 0),
                Failures = Interlocked.CompareExchange(ref _failures, 0, 0),
                AverageDurationMs = _executions > 0
                    ? (double)Interlocked.CompareExchange(ref _totalDurationMs, 0, 0) / _executions
                    : 0,
                ActionCounts = new Dictionary<string, int>(_actionCounts),
                ErrorCounts = new Dictionary<ErrorCode, int>(_errorCounts)
            };
        }
    }

    #endregion
}
