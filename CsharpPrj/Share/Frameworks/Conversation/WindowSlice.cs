using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation;

/// <summary>
/// 尾部窗口切片**唯一实现**（RP-3）：`take` 归一 + 尾部截取。
/// <para>Frame <see cref="ConversationLog"/> 与 Agent 侧窗口读取共用本实现（Agent.Contract 的同名门面为本类转发）。</para>
/// </summary>
public static class WindowSlice
{
    /// <summary>取尾部 <paramref name="take"/> 条；<paramref name="take"/> ≤ 0 归一为 1（与既有窗口语义一致）。</summary>
    public static IReadOnlyList<T> TakeLast<T>(IReadOnlyList<T> source, int take)
    {
        ArgumentNullException.ThrowIfNull(source);
        var limit = Math.Max(1, take);
        if (source.Count <= limit) return source;

        var result = new List<T>(limit);
        for (var i = source.Count - limit; i < source.Count; i++) result.Add(source[i]);
        return result;
    }

    /// <summary>取尾部 <paramref name="take"/> 条（可枚举源；有界缓冲）。</summary>
    public static IReadOnlyList<T> TakeLast<T>(IEnumerable<T> source, int take)
    {
        ArgumentNullException.ThrowIfNull(source);
        var limit = Math.Max(1, take);
        return source.TakeLast(limit).ToList();
    }
}
