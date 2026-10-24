#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 段别名归一（唯一权威）—— 由段声明面的 <c>aliases=</c> 派生，禁止在流程里手写第二张别名表。
/// <para>端口键与段键的映射：<c>system→prompt.l0</c>、<c>l1→prompt.l1</c>、<c>l2→prompt.l2</c>；
/// 其余段键原样。别名方向单一（段 → 兼容来源），反向归一由「按键查表」完成。</para>
/// </summary>
public static class PromptSegmentAliases
{
    /// <summary>段键 → 端口键（装配段与流程端口同义不同名）。</summary>
    public static string PortKeyOf(string segmentKey)
    {
        if (string.IsNullOrWhiteSpace(segmentKey)) return string.Empty;
        if (!segmentKey.StartsWith("prompt.", StringComparison.Ordinal)) return segmentKey;
        var rest = segmentKey["prompt.".Length..];
        return string.Equals(rest, "l0", StringComparison.Ordinal) ? "system" : rest;
    }

    /// <summary>端口键 → 兼容来源键（顺序即优先级；含反向：声明别名命中时回落 canonical 端口）。</summary>
    public static IReadOnlyList<string> Of(string portKey)
    {
        if (string.IsNullOrWhiteSpace(portKey)) return Array.Empty<string>();
        foreach (var row in PromptSegmentCatalog.Rows)
        {
            if (row.Aliases is not { Count: > 0 }) continue;
            if (!string.Equals(PortKeyOf(row.Key), portKey, StringComparison.Ordinal)) continue;
            return row.Aliases;
        }

        // 反向：portKey 本身是某段的兼容来源（如 session_input）⇒ 可回落该段 canonical 端口。
        foreach (var row in PromptSegmentCatalog.Rows)
        {
            if (row.Aliases is not { Count: > 0 }) continue;
            foreach (var alias in row.Aliases)
                if (string.Equals(alias, portKey, StringComparison.Ordinal))
                    return new[] { PortKeyOf(row.Key) };
        }

        return Array.Empty<string>();
    }

    /// <summary>全部端口 → 别名映射（声明面缺失别名的端口不出现）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> All
        => PromptSegmentCatalog.Rows
            .Where(static r => r.Aliases is { Count: > 0 })
            .GroupBy(static r => PortKeyOf(r.Key), StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First().Aliases!, StringComparer.Ordinal);
}
