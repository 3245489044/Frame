using System.Linq;

namespace Share.Messaging;

/// <summary>
/// 只读计数观测器（线程安全；不参与决策）。
/// <para>计数面：Sent/Claimed/Acked/Failed/Expired/DeadLetter/Duplicate + 每画像发送数。</para>
/// </summary>
public sealed class MessageObserver : IMessageObserver
{
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _byCode = new(StringComparer.Ordinal);
    private readonly Dictionary<int, long> _hopBuckets = new();
    private readonly Dictionary<string, int> _queueDepth = new(StringComparer.Ordinal);
    private readonly Queue<double> _latency = new();
    private long _sent;
    private long _acked;
    private long _failed;
    private long _expired;
    private long _deadLetter;
    private long _claimed;
    private long _duplicate;
    private const int LatencyCapacity = 512;

    /// <summary>空观测器（未装配时使用，零分配副作用）。</summary>
    public static MessageObserver Null { get; } = new();

    /// <inheritdoc />
    public void OnSent(MessageEnvelope message, DeliveryClass delivery)
    {
        Interlocked.Increment(ref _sent);
        if (message.State == MessageState.Claimed) Interlocked.Increment(ref _claimed);
        Count("delivery." + delivery.ToString());

        // hop 分布：0..2 精确分桶，3+ 归入最后一桶（有界，禁无界基数）。
        var bucket = message.Hop <= 2 ? message.Hop : 3;
        lock (_sync)
        {
            _hopBuckets.TryGetValue(bucket, out var current);
            _hopBuckets[bucket] = current + 1;
        }
    }

    /// <inheritdoc />
    public void OnLatency(string messageId, double milliseconds)
    {
        if (milliseconds < 0) return;
        lock (_sync)
        {
            if (_latency.Count >= LatencyCapacity) _latency.Dequeue();
            _latency.Enqueue(milliseconds);
        }
    }

    /// <inheritdoc />
    public void OnQueueDepth(string scope, int depth)
    {
        if (string.IsNullOrWhiteSpace(scope) || depth < 0) return;
        lock (_sync)
        {
            _queueDepth.TryGetValue(scope, out var current);
            if (depth > current) _queueDepth[scope] = depth;
        }
    }

    /// <inheritdoc />
    public void OnTransition(string messageId, MessageState from, MessageState to, string? code = null)
    {
        switch (to)
        {
            case MessageState.Acked: Interlocked.Increment(ref _acked); break;
            case MessageState.Failed: Interlocked.Increment(ref _failed); break;
            case MessageState.Expired: Interlocked.Increment(ref _expired); break;
            case MessageState.DeadLetter: Interlocked.Increment(ref _deadLetter); break;
            case MessageState.Claimed: Interlocked.Increment(ref _claimed); break;
        }

        if (string.Equals(code, "duplicate", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _duplicate);
    }

    /// <summary>计数快照（只读）。</summary>
    public IReadOnlyDictionary<string, long> Snapshot()
    {
        var snapshot = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["sent"] = Interlocked.Read(ref _sent),
            ["claimed"] = Interlocked.Read(ref _claimed),
            ["acked"] = Interlocked.Read(ref _acked),
            ["failed"] = Interlocked.Read(ref _failed),
            ["expired"] = Interlocked.Read(ref _expired),
            ["deadletter"] = Interlocked.Read(ref _deadLetter),
            ["duplicate"] = Interlocked.Read(ref _duplicate),
        };

        lock (_sync)
        {
            foreach (var pair in _byCode) snapshot[pair.Key] = pair.Value;

            long queueMax = 0;
            foreach (var pair in _queueDepth)
            {
                snapshot["queue." + pair.Key] = pair.Value;
                if (pair.Value > queueMax) queueMax = pair.Value;
            }

            snapshot["queue.max"] = queueMax;

            foreach (var pair in _hopBuckets) snapshot["hop.h" + pair.Key] = pair.Value;

            if (_latency.Count > 0)
            {
                var ordered = _latency.OrderBy(v => v).ToArray();
                snapshot["latency.p50"] = (long)Math.Round(Percentile(ordered, 0.50));
                snapshot["latency.p95"] = (long)Math.Round(Percentile(ordered, 0.95));
            }
            else
            {
                snapshot["latency.p50"] = 0;
                snapshot["latency.p95"] = 0;
            }
        }

        return snapshot;
    }

    private static double Percentile(double[] ordered, double p)
    {
        if (ordered.Length == 0) return 0;
        var index = (int)Math.Ceiling(p * ordered.Length) - 1;
        if (index < 0) index = 0;
        if (index >= ordered.Length) index = ordered.Length - 1;
        return ordered[index];
    }

    private void Count(string key)
    {
        lock (_sync)
        {
            _byCode.TryGetValue(key, out var current);
            _byCode[key] = current + 1;
        }
    }
}
