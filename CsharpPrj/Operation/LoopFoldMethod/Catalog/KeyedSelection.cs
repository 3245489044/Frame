using System;
using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Catalog;

/// <summary>
/// **键选择合并**（通用；纯函数）—— 把「原序键集」与「显式序覆盖列表」合并：
/// 显式列表在前的按显式序，其余按原序追加（**不丢行**）。
/// </summary>
public static class KeyedSelection
{
    /// <summary>合并显式序与原始序（<paramref name="order"/> 空 ⇒ 原样返回）。</summary>
    /// <param name="keys">原始序键集。</param>
    /// <param name="order">显式序覆盖列表（可空）。</param>
    public static IReadOnlyList<string> MergeExplicitOrder(
        IReadOnlyList<string> keys, IReadOnlyList<string>? order)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (order is not { Count: > 0 })
        {
            return keys;
        }

        var remaining = new HashSet<string>(keys, StringComparer.Ordinal);
        var ordered = new List<string>(keys.Count);
        foreach (var key in order)
        {
            if (remaining.Remove(key))
            {
                ordered.Add(key);
            }
        }

        // 未在显式列表中的键按原（序数）序追加，保证不丢行。
        foreach (var key in keys)
        {
            if (remaining.Contains(key))
            {
                ordered.Add(key);
            }
        }

        return ordered;
    }
}
