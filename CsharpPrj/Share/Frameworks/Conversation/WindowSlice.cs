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

    /// <summary>
    /// 尾部窗口切片（分页口径）：跳过末尾 <paramref name="skipFromEnd"/> 条后，取紧邻的 <paramref name="take"/> 条。
    /// <para>全仓「尾部跳过 + 取 N」的唯一实现；调用点禁手写 <c>Skip(count - take - skip)</c>。</para>
    /// </summary>
    /// <param name="source">源序列。</param>
    /// <param name="take">条数（≤0 ⇒ 归一为 1，与既有窗口语义一致）。</param>
    /// <param name="skipFromEnd">离尾跳过条数（&lt;0 ⇒ 0）。</param>
    public static IReadOnlyList<T> TakeLast<T>(IReadOnlyList<T> source, int take, int skipFromEnd)
    {
        ArgumentNullException.ThrowIfNull(source);
        var limit = Math.Max(1, take);
        var skip = Math.Max(0, skipFromEnd);
        var end = source.Count - skip;
        if (end <= 0) return Array.Empty<T>();

        var start = Math.Max(0, end - limit);
        if (start == 0 && end == source.Count) return source;

        var result = new List<T>(end - start);
        for (var i = start; i < end; i++) result.Add(source[i]);
        return result;
    }
}
