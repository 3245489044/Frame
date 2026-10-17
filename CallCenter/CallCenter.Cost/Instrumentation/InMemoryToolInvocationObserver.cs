using CallCenter.Cost.Metering;

namespace CallCenter.Cost.Instrumentation;

/// <summary>
/// 内存观察者：唯一计数实现（幂等 Complete / Retry 只增物理数 / 取消超时计数）。
/// 落库由上层按快照决定（默认只聚合）。
/// </summary>
public sealed class InMemoryToolInvocationObserver : IToolInvocationObserver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ToolInvocationScope> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LogicalState> _logical = new(StringComparer.Ordinal);
    private readonly List<ToolInvocation> _invocations = new();
    private long _sequence;

    public ToolInvocationScope Begin(ToolInvocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sequence = Interlocked.Increment(ref _sequence);
        var toolCallId = string.IsNullOrWhiteSpace(request.TraceId)
            ? $"{request.ToolName}:{sequence}"
            : $"{request.TraceId}:{sequence}";
        var logicalCallId = string.IsNullOrWhiteSpace(request.LogicalCallId)
            ? toolCallId
            : request.LogicalCallId!;

        var scope = new ToolInvocationScope(this, request, toolCallId, logicalCallId, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _scopes[toolCallId] = scope;
            if (!_logical.TryGetValue(logicalCallId, out var state))
            {
                state = new LogicalState(logicalCallId);
                _logical[logicalCallId] = state;
            }

            state.PhysicalCount++;
        }

        return scope;
    }

    public void Complete(ToolInvocationScope scope, ToolOutcome outcome, ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!scope.MarkCompleted())
        {
            return;
        }

        scope.Stopwatch.Stop();
        lock (_gate)
        {
            if (!_logical.TryGetValue(scope.LogicalCallId, out var state))
            {
                return;
            }

            state.ApplyOutcome(outcome);
            state.WallMsTotal += scope.ElapsedMs;
            if (usage?.Compute is not null)
            {
                state.Compute = usage.Compute;
            }

            var aggregate = state.ToUsage();
            _invocations.Add(new ToolInvocation(
                scope.ToolCallId,
                scope.Request.TraceId,
                scope.Request.ParentSpanId,
                scope.Request.ToolName,
                scope.Request.Medium,
                scope.Request.Origin,
                scope.Request.Instance,
                scope.Request.ArgsHash,
                scope.Request.CallSite,
                scope.Request.TaskKey,
                scope.Request.RoundId,
                scope.Request.SessionKey,
                scope.StartedAt,
                scope.ElapsedMs,
                outcome,
                scope.Attempt,
                scope.LogicalCallId,
                aggregate,
                Cost: null,
                scope.Request.Attributes));
            _scopes.Remove(scope.ToolCallId);
        }
    }

    public void Retry(ToolInvocationScope scope, int attempt)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            if (!_logical.TryGetValue(scope.LogicalCallId, out var state))
            {
                return;
            }

            var next = Math.Max(attempt, scope.Attempt + 1);
            scope.Attempt = next;
            state.PhysicalCount++;
            state.RetryCount++;
        }
    }

    public void Abandon(ToolInvocationScope scope, string reason)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var outcome = reason?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true
            ? ToolOutcome.Timeout
            : ToolOutcome.Cancelled;
        Complete(scope, outcome, ToolCallUsage.Empty);
    }

    /// <summary>全部逻辑调用的聚合计数。</summary>
    public ToolCallUsage Snapshot()
    {
        lock (_gate)
        {
            var all = _logical.Values.Select(s => s.ToUsage()).ToList();
            if (all.Count == 0)
            {
                return ToolCallUsage.Empty;
            }

            return new ToolCallUsage(
                all.Sum(u => u.PhysicalCount),
                all.Count,
                all.Sum(u => u.RetryCount),
                all.Sum(u => u.SuccessCount),
                all.Sum(u => u.FailureCount),
                all.Sum(u => u.TimeoutCount),
                all.Sum(u => u.CancelledCount),
                all.Sum(u => u.WallMsTotal),
                all.Select(u => u.Compute).FirstOrDefault(c => c is not null));
        }
    }

    /// <summary>已结算调用明细快照。</summary>
    public IReadOnlyList<ToolInvocation> SnapshotInvocations()
    {
        lock (_gate)
        {
            return _invocations.ToList();
        }
    }

    private sealed class LogicalState
    {
        public LogicalState(string logicalCallId) => LogicalCallId = logicalCallId;

        public string LogicalCallId { get; }
        public int PhysicalCount { get; set; }
        public int RetryCount { get; set; }
        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }
        public int TimeoutCount { get; private set; }
        public int CancelledCount { get; private set; }
        public long WallMsTotal { get; set; }
        public ComputeUsage? Compute { get; set; }

        public void ApplyOutcome(ToolOutcome outcome)
        {
            switch (outcome)
            {
                case ToolOutcome.Success: SuccessCount++; break;
                case ToolOutcome.Failure: FailureCount++; break;
                case ToolOutcome.Timeout: TimeoutCount++; break;
                case ToolOutcome.Cancelled: CancelledCount++; break;
            }
        }

        public ToolCallUsage ToUsage() => new(
            PhysicalCount,
            1,
            RetryCount,
            SuccessCount,
            FailureCount,
            TimeoutCount,
            CancelledCount,
            WallMsTotal,
            Compute);
    }
}
