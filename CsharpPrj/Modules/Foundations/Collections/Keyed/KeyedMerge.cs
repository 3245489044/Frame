namespace Modules.Foundations.Collections.Keyed;

/// <summary>
/// 键去重合并 + 稳定排序（通用；纯函数）—— 同一事实多源到达时的**唯一合并原语**。
/// <para>
/// 语义：按调用方给出的键选择器去重；重复项交给调用方的合并函数（左 = 先到者，优先级更高）；
/// 排序为**稳定序**（同比较值保持源序），禁以 Linq 默认排序替代。
/// </para>
/// <para>零领域词：键选择器与合并策略全部由调用方注入，本件不认识任何业务类型。</para>
/// </summary>
public static class KeyedMerge
{
    /// <summary>
    /// 按键去重并合并重复项（先到者胜为合并函数的左参）。
    /// </summary>
    /// <typeparam name="T">条目类型（引用类型）。</typeparam>
    /// <param name="items">源序列（null ⇒ 空结果）。</param>
    /// <param name="keyOf">键选择器（须稳定；禁含执行者叙述）。</param>
    /// <param name="merge">重复项合并函数（左 = 已收集条目）。</param>
    public static List<T> DistinctMerge<T>(
        IEnumerable<T>? items,
        Func<T, string> keyOf,
        Func<T, T, T> merge)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(merge);

        var result = new List<T>();
        if (items is null) return result;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null) continue;
            var key = keyOf(item) ?? string.Empty;
            if (index.TryGetValue(key, out var existing))
            {
                result[existing] = merge(result[existing], item);
                continue;
            }

            index[key] = result.Count;
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// 稳定排序（同比较值保持源序；空序列 ⇒ 空结果）。
    /// </summary>
    /// <typeparam name="T">条目类型。</typeparam>
    /// <param name="items">源序列。</param>
    /// <param name="comparison">比较函数。</param>
    public static List<T> StableOrder<T>(IEnumerable<T>? items, Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var source = items is null ? new List<T>() : new List<T>(items);
        if (source.Count <= 1) return source;

        // List.Sort 非稳定：用源序索引做次级比较，保证「同序保持源序」。
        var indexed = new List<(T Item, int Index)>(source.Count);
        for (var i = 0; i < source.Count; i++) indexed.Add((source[i], i));

        indexed.Sort((left, right) =>
        {
            var order = comparison(left.Item, right.Item);
            return order != 0 ? order : left.Index.CompareTo(right.Index);
        });

        var ordered = new List<T>(indexed.Count);
        foreach (var entry in indexed) ordered.Add(entry.Item);
        return ordered;
    }
}
