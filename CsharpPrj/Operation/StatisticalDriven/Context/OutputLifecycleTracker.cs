using System.Collections.Concurrent;

namespace Operation.StatisticalDriven.Context;

/// <summary>
/// 输出生命周期追踪（设计文档 6.2）：为每段输出记录首注入轮次与累计重发次数。
/// 纯状态、无 I/O、线程安全；事件化由调用方经统计语义事件发出。
/// </summary>
public sealed class OutputLifecycleTracker
{
    private sealed class Entry
    {
        public required string OutputId { get; init; }
        public required string FirstTurnId { get; init; }
        public required long FirstTokens { get; init; }
        public long RepeatCount;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>记录某段输出首次注入上下文；已存在则忽略（首注入不覆盖）。</summary>
    public void RecordEmitted(string outputId, string turnId, long tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        _entries.TryAdd(outputId, new Entry { OutputId = outputId, FirstTurnId = turnId, FirstTokens = tokens });
    }

    /// <summary>记录一次重发；返回该输出的累计重复次数（首见则按首次注入建条目并返回 1）。</summary>
    public long RecordReinjected(string outputId, string turnId, long tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        var entry = _entries.GetOrAdd(outputId,
            id => new Entry { OutputId = id, FirstTurnId = turnId, FirstTokens = tokens });
        return Interlocked.Increment(ref entry.RepeatCount);
    }

    public bool TryGet(string outputId, out string firstTurnId, out long repeatCount, out long firstTokens)
    {
        if (_entries.TryGetValue(outputId, out var e))
        {
            firstTurnId = e.FirstTurnId;
            repeatCount = Volatile.Read(ref e.RepeatCount);
            firstTokens = e.FirstTokens;
            return true;
        }
        firstTurnId = "";
        repeatCount = 0;
        firstTokens = 0;
        return false;
    }

    public IReadOnlyCollection<string> OutputIds => _entries.Keys.ToArray();
}