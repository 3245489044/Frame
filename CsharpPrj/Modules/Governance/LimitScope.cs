namespace Modules.Governance;

/// <summary>
/// 单个作用域的计量与预留容器（唯一写者：内核）。
/// 只存计数与样本，不复制父计数器，避免「父子双份、对不上」。
/// </summary>
internal sealed class LimitScope
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    internal LimitScope(string id, LimitScopeKind kind, LimitScope? parent)
    {
        Id = id;
        Kind = kind;
        Parent = parent;
        StartedAt = DateTimeOffset.UtcNow;
    }

    internal string Id { get; }

    internal LimitScopeKind Kind { get; }

    internal LimitScope? Parent { get; }

    internal DateTimeOffset StartedAt { get; }

    internal string? ParentId => Parent?.Id;

    /// <summary>取（或建）资源计数器；调用方须持有本 scope 的临界区。</summary>
    internal Counter GetOrAdd(string resource)
    {
        if (!_counters.TryGetValue(resource, out var c))
        {
            c = new Counter();
            _counters[resource] = c;
        }
        return c;
    }

    internal bool TryGet(string resource, out Counter counter)
        => _counters.TryGetValue(resource, out counter!);

    internal IReadOnlyList<KeyValuePair<string, Counter>> Counters()
    {
        lock (_gate)
            return _counters.ToArray();
    }

    /// <summary>清空本作用域全部计数（Reset 用）。</summary>
    internal void Clear()
    {
        lock (_gate) _counters.Clear();
    }

    /// <summary>在 scope 临界区内执行读改写。</summary>
    internal T WithLock<T>(Func<T> body)
    {
        lock (_gate) return body();
    }

    internal void WithLock(Action body)
    {
        lock (_gate) body();
    }

    /// <summary>资源计数器：生命周期已用 / 预留 / 并发占用 / Unknown 计数 / 窗口样本。</summary>
    internal sealed class Counter
    {
        internal long Used;
        internal long Reserved;
        internal int Active;
        internal long Unknown;
        internal readonly List<(DateTimeOffset At, long Amount)> Samples = new();

        /// <summary>窗口内已提交用量（按样本时间过滤；调用方须持锁）。</summary>
        internal long UsedInWindow(DateTimeOffset now, long windowSeconds)
        {
            if (windowSeconds <= 0) return Used;
            var cutoff = now.AddSeconds(-windowSeconds);
            long sum = 0;
            for (var i = 0; i < Samples.Count; i++)
                if (Samples[i].At >= cutoff) sum += Samples[i].Amount;
            return sum;
        }

        /// <summary>清理超出最长保留窗口的样本（默认 24h，防长跑内存增长）。</summary>
        internal void Trim(DateTimeOffset now)
        {
            var cutoff = now.AddHours(-24);
            if (Samples.Count == 0 || Samples[0].At >= cutoff) return;
            Samples.RemoveAll(s => s.At < cutoff);
        }
    }
}
