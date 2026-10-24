#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 双原语选择（纯函数，无副作用）：大变更 ⇒ <see cref="SectionWriteMode.BulkReplace"/>，小变更 ⇒ Patch。
/// <para>判据载体 = 结构事实（节点数 / 键集合差），不由领域自述决定。</para>
/// </summary>
public static class BulkThresholdPolicy
{
    /// <summary>模式判定：<paramref name="eligible"/>=false（非 HTML 节/volatile 节）恒 Patch。</summary>
    public static SectionWriteMode Decide(
        bool eligible, int nodes, double changeRatio, int bulkNodeThreshold, double bulkRatioThreshold)
    {
        if (!eligible) return SectionWriteMode.Patch;
        if (nodes > bulkNodeThreshold) return SectionWriteMode.BulkReplace;
        if (changeRatio > bulkRatioThreshold) return SectionWriteMode.BulkReplace;
        return SectionWriteMode.Patch;
    }

    /// <summary>键集合差比：|new Δ old| / |new ∪ old|（空并集 ⇒ 0，禁除零）。</summary>
    public static double ChangeRatio(IReadOnlyCollection<string>? oldKeys, IReadOnlyCollection<string>? newKeys)
    {
        var oldSet = oldKeys is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(oldKeys, StringComparer.Ordinal);
        var newSet = newKeys is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(newKeys, StringComparer.Ordinal);
        if (oldSet.Count == 0 && newSet.Count == 0) return 0;

        var union = new HashSet<string>(oldSet, StringComparer.Ordinal);
        union.UnionWith(newSet);
        var diff = 0;
        foreach (var k in oldSet)
        {
            if (!newSet.Contains(k)) diff++;
        }

        foreach (var k in newSet)
        {
            if (!oldSet.Contains(k)) diff++;
        }

        return union.Count == 0 ? 0 : (double)diff / union.Count;
    }

    /// <summary>收集键控树的稳定键集合（O(n)，不遍历 DOM）。</summary>
    public static IReadOnlyCollection<string> CollectKeys(UiNode? tree)
    {
        var keys = new List<string>();
        Walk(tree, keys);
        return keys;
    }

    private static void Walk(UiNode? node, List<string> keys)
    {
        if (node is null) return;
        if (!string.IsNullOrEmpty(node.Key)) keys.Add(node.Key!);
        if (node.Children is null) return;
        foreach (var child in node.Children) Walk(child, keys);
    }
}
