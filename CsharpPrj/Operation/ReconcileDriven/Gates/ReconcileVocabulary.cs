using Strategy.Observability.Registry;

namespace Operation.ReconcileDriven.Gates;

/// <summary>
/// 对账语义词表：可接受的 EventKind 集合（来自观测契约注册表 / 已加载语义契约）。
/// <para>用途：装载期判据 C1（signal 须在词表内）；失配时给出邻近候选，治「词表漂移难排查」。</para>
/// </summary>
public sealed class ReconcileVocabulary
{
    private readonly HashSet<string> _kinds;

    public ReconcileVocabulary(IEnumerable<string> kinds)
        => _kinds = new HashSet<string>(kinds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

    /// <summary>词表大小。</summary>
    public int Count => _kinds.Count;

    /// <summary>词表是否为空（空词表 ⇒ C1 跳过，避免无契约场景误拒载）。</summary>
    public bool IsEmpty => _kinds.Count == 0;

    /// <summary>是否含某 EventKind。</summary>
    public bool Contains(string? signal)
        => !string.IsNullOrWhiteSpace(signal) && _kinds.Contains(signal!);

    /// <summary>邻近候选（含子串双向包含；供报错提示）。</summary>
    public IReadOnlyList<string> NearCandidates(string? signal, int max = 5)
    {
        var s = signal ?? string.Empty;
        return _kinds
            .Where(k => k.Contains(s, StringComparison.OrdinalIgnoreCase)
                        || s.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Take(max)
            .ToList();
    }

    /// <summary>从观测契约集合构造。</summary>
    public static ReconcileVocabulary FromContracts(IEnumerable<EventContract> contracts)
        => new(contracts.Select(c => c.EventKind));

    /// <summary>从注册表构造（含全部已注册 EventKind）。</summary>
    public static ReconcileVocabulary FromRegistry(IObservabilityContractRegistry registry)
        => new(registry.Contracts.Select(c => c.EventKind));

    /// <summary>空词表。</summary>
    public static ReconcileVocabulary Empty { get; } = new(Array.Empty<string>());
}
