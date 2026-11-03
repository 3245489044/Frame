namespace Usharp.Semantics;

/// <summary>重复 id 的取舍策略（必须显式声明，禁止各处自行 First/Last）。</summary>
public enum UsharpDuplicatePolicy
{
    /// <summary>保留首个声明（默认，与既有闸A/产物口径一致）。</summary>
    FirstWins,

    /// <summary>保留最后声明（仅用于明确要求的覆盖语义）。</summary>
    LastWins,
}

/// <summary>一次重复声明的出现位置。</summary>
public sealed record UsharpDuplicateOccurrence<T>(T Item, UsharpSourceRef At) where T : class;

/// <summary>同一 id 的重复组：winner + 全部出现位置（声明序）。</summary>
public sealed record UsharpDuplicateGroup<T>(
    string Id, T Winner, IReadOnlyList<UsharpDuplicateOccurrence<T>> Occurrences) where T : class;

/// <summary>
/// 声明 id 索引内核（**单一实现**）：一次建索引，Facts / 闸A / 产物共用同一 winner；
/// 重复项由 <see cref="Duplicates"/> 暴露，并由闸A 归因到**重复处自身**（不再报首次声明的位置）。
/// </summary>
public sealed class UsharpDeclarationIdIndex<T> where T : class
{
    private readonly Dictionary<string, T> _byId;

    private UsharpDeclarationIdIndex(
        IReadOnlyList<T> all,
        Dictionary<string, T> byId,
        IReadOnlyList<UsharpDuplicateGroup<T>> duplicates)
    {
        All = all;
        _byId = byId;
        Duplicates = duplicates;
    }

    /// <summary>全部条目（声明序）。</summary>
    public IReadOnlyList<T> All { get; }

    /// <summary>重复组（声明序）。</summary>
    public IReadOnlyList<UsharpDuplicateGroup<T>> Duplicates { get; }

    /// <summary>按 id 取 winner（空白 id ⇒ false）。</summary>
    public bool TryGet(string? id, out T value)
    {
        if (!string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id, out var found))
        {
            value = found;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>索引内全部 id（声明序去重）。</summary>
    public IReadOnlyList<string> Ids => _byId.Keys.ToList();

    /// <summary>
    /// 构建索引。
    /// </summary>
    /// <param name="items">声明集（声明序）。</param>
    /// <param name="idOf">取 id（空白 ⇒ 不入索引，由各自形状规则报错）。</param>
    /// <param name="whereOf">取归因位置。</param>
    /// <param name="policy">重复取舍策略。</param>
    public static UsharpDeclarationIdIndex<T> Build(
        IEnumerable<T> items,
        Func<T, string?> idOf,
        Func<T, UsharpSourceRef> whereOf,
        UsharpDuplicatePolicy policy = UsharpDuplicatePolicy.FirstWins)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(idOf);
        ArgumentNullException.ThrowIfNull(whereOf);

        var all = new List<T>();
        var ordered = new Dictionary<string, List<UsharpDuplicateOccurrence<T>>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var item in items)
        {
            if (item is null) continue;
            all.Add(item);

            var id = idOf(item);
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (!ordered.TryGetValue(id, out var occurrences))
            {
                ordered[id] = occurrences = new List<UsharpDuplicateOccurrence<T>>();
                order.Add(id);
            }

            occurrences.Add(new UsharpDuplicateOccurrence<T>(item, whereOf(item)));
        }

        var byId = new Dictionary<string, T>(StringComparer.Ordinal);
        var duplicates = new List<UsharpDuplicateGroup<T>>();
        foreach (var id in order)
        {
            var occurrences = ordered[id];
            var winner = policy == UsharpDuplicatePolicy.LastWins ? occurrences[^1].Item : occurrences[0].Item;
            byId[id] = winner;
            if (occurrences.Count > 1) duplicates.Add(new UsharpDuplicateGroup<T>(id, winner, occurrences));
        }

        return new UsharpDeclarationIdIndex<T>(all, byId, duplicates);
    }
}
