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
    private IDisposable? _restore;

    public TriggerScope(string triggerId)
        => TriggerId = string.IsNullOrWhiteSpace(triggerId) ? "(unknown)" : triggerId;

    /// <summary>触发标识（函数名/控件名；用于裁决落账）。</summary>
    public string TriggerId { get; }

    /// <summary>挂接 ambient 恢复句柄（由收集器 Begin 注入；Close 时恢复上一层域）。</summary>
    internal void AttachRestore(IDisposable restore) => _restore = restore;

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

    /// <summary>是否已收任一指定 EventKind（锁内扫描，不复制快照；轮询路径用）。</summary>
    public bool ContainsAny(IReadOnlySet<string> eventKinds)
    {
        if (eventKinds.Count == 0) return false;
        lock (_gate)
        {
            foreach (var record in _records)
                if (eventKinds.Contains(record.EventKind)) return true;
            return false;
        }
    }

    /// <summary>关闭窗口并产出信号账（幂等：重复 Close 返回同一快照；同时恢复上一层 ambient 域）。</summary>
    public SignalTrace Close()
    {
        SignalTrace trace;
        IDisposable? restore;
        lock (_gate)
        {
            _closed = true;
            trace = new SignalTrace(_records.ToList());
            restore = _restore;
            _restore = null;
        }

        // 在锁外恢复 ambient：Dispose 中的 AsyncLocal 写入不占域锁，避免与 Push 争用。
        restore?.Dispose();
        return trace;
    }

    public void Dispose() => Close();
}
