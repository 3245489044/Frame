namespace Operation.ReconcileDriven.Trace;

/// <summary>
/// 触发域句柄：一次触发的信号收集窗口（Begin → 执行 → Close 产出 <see cref="SignalTrace"/>）。
/// 线程/异步流安全由 <see cref="TriggerScopedSignalCollector"/> 的 AsyncLocal 承载（跨 await 流动）。
/// </summary>
public sealed class TriggerScope : IDisposable
{
    private readonly List<SignalRecord> _records = new();
    private readonly object _gate = new();
    private bool _closed;

    public TriggerScope(string triggerId)
        => TriggerId = string.IsNullOrWhiteSpace(triggerId) ? "(unknown)" : triggerId;

    /// <summary>触发标识（函数名/控件名；用于裁决落账）。</summary>
    public string TriggerId { get; }

    /// <summary>已收集条数。</summary>
    public int Count { get { lock (_gate) return _records.Count; } }

    internal void Add(SignalRecord record)
    {
        lock (_gate)
        {
            if (_closed) return;
            _records.Add(record);
        }
    }

    /// <summary>当前已收记录的只读快照（不关闭窗口；供终止等待窗轮询）。</summary>
    public IReadOnlyList<SignalRecord> Peek()
    {
        lock (_gate) return _records.ToList();
    }

    /// <summary>关闭窗口并产出信号账（幂等：重复 Close 返回同一快照）。</summary>
    public SignalTrace Close()
    {
        lock (_gate)
        {
            _closed = true;
            return new SignalTrace(_records.ToList());
        }
    }

    public void Dispose() => Close();
}
