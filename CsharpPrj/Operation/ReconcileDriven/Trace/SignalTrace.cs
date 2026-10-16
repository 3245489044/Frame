namespace Operation.ReconcileDriven.Trace;

/// <summary>
/// 触发域信号账（SignalTrace）：单次 UI 触发期间、同作用域下同步收集的信号有序集合。
/// <para>顺序 = 收集序（<see cref="Strategy.Observability.Bus.ObservabilityBus.Emit"/> 在锁内串行 Push，顺序即时序）。</para>
/// </summary>
public sealed class SignalTrace
{
    private readonly List<SignalRecord> _records;

    public SignalTrace(IEnumerable<SignalRecord>? records = null)
        => _records = records?.ToList() ?? new List<SignalRecord>();

    /// <summary>有序信号记录（只读）。</summary>
    public IReadOnlyList<SignalRecord> Records => _records;

    /// <summary>记录条数。</summary>
    public int Count => _records.Count;

    /// <summary>是否含某 EventKind。</summary>
    public bool Contains(string eventKind)
        => _records.Any(r => string.Equals(r.EventKind, eventKind, StringComparison.Ordinal));

    /// <summary>某 EventKind 首次出现的下标（不存在返回 -1）。</summary>
    public int IndexOf(string eventKind)
    {
        for (var i = 0; i < _records.Count; i++)
            if (string.Equals(_records[i].EventKind, eventKind, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>某 EventKind 首条记录（不存在返回 null）。</summary>
    public SignalRecord? First(string eventKind)
        => _records.FirstOrDefault(r => string.Equals(r.EventKind, eventKind, StringComparison.Ordinal));

    /// <summary>空账（无信号）。</summary>
    public static SignalTrace Empty { get; } = new();
}
