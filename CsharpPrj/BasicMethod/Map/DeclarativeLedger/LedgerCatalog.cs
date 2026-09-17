namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 单段键控目录：确定性排序（键 Ordinal 升序）、键唯一（重复键即抛）、原文随行（可空）。
/// <para>
/// 只承载**单段单件**模式：多段拼成一个目录外观（四视图组合）留消费方，框架不预设段数。
/// </para>
/// </summary>
public sealed class LedgerCatalog<TEntry>
{
    private readonly Dictionary<string, TEntry> _byKey;

    /// <param name="entries">条目集（入目录时按键 Ordinal 升序重排，与输入顺序无关）。</param>
    /// <param name="keySelector">键提取（键须唯一；重复即 <see cref="ArgumentException"/>）。</param>
    /// <param name="rawJson">原文（供只吃文本的消费方；可空 = 无原文）。</param>
    public LedgerCatalog(IReadOnlyList<TEntry> entries, Func<TEntry, string> keySelector, string? rawJson = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(keySelector);

        Entries = [.. entries.OrderBy(keySelector, StringComparer.Ordinal)];
        Keys = [.. Entries.Select(keySelector)];
        _byKey = new Dictionary<string, TEntry>(Entries.Count, StringComparer.Ordinal);
        foreach (var entry in Entries) _byKey.Add(keySelector(entry), entry);
        RawJson = rawJson;
    }

    /// <summary>条目（键 Ordinal 升序）。</summary>
    public IReadOnlyList<TEntry> Entries { get; }

    /// <summary>键集（同序）。</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>原文（可空）。</summary>
    public string? RawJson { get; }

    /// <summary>按键取条目（未登记 → null）。</summary>
    public TEntry? Find(string key) => _byKey.GetValueOrDefault(key);
}
