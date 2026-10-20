namespace Share.Messaging;

/// <summary>
/// 只读计数观测器（线程安全；不参与决策）。
/// <para>计数面：Sent/Claimed/Acked/Failed/Expired/DeadLetter/Duplicate + 每画像发送数。</para>
/// </summary>
public sealed class MessageObserver : IMessageObserver
{
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _byCode = new(StringComparer.Ordinal);
    private long _sent;
    private long _acked;
    private long _failed;
    private long _expired;
    private long _deadLetter;
    private long _claimed;
    private long _duplicate;

    /// <summary>空观测器（未装配时使用，零分配副作用）。</summary>
    public static MessageObserver Null { get; } = new();

    /// <inheritdoc />
    public void OnSent(MessageEnvelope message, DeliveryClass delivery)
    {
        Interlocked.Increment(ref _sent);
        if (message.State == MessageState.Claimed) Interlocked.Increment(ref _claimed);
        Count("delivery." + delivery.ToString());
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
        }

        return snapshot;
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
