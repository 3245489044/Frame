#nullable enable

using System.Collections.Generic;
using System.Text;

namespace Share.Composition.Text;

/// <summary>
/// 文本拼接唯一入口 —— 片段序列 + <see cref="JoinPolicy"/> → 文本。
/// <para>语义要点：分隔符按「已输出片段数」插入（保留空段时与 <c>string.Join</c> 逐字一致），
/// 空段判据与裁剪口径全部来自策略，调用方不得自行判空或 Trim。</para>
/// </summary>
public static class TextJoin
{
    /// <summary>按策略拼接片段（null 序列 → 空串）。</summary>
    public static string Apply(IEnumerable<string?>? parts, JoinPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (parts is null) return string.Empty;

        var sb = new StringBuilder();
        var first = true;
        foreach (var raw in parts)
        {
            var part = Normalize(raw, policy.Trim);
            if (policy.Empty == EmptyPolicy.DropEmpty && string.IsNullOrEmpty(part)) continue;
            if (policy.Empty == EmptyPolicy.DropBlank && string.IsNullOrWhiteSpace(part)) continue;

            if (!first) sb.Append(policy.Separator);
            sb.Append(part);
            first = false;
        }
        return sb.ToString();
    }

    /// <summary>按策略筛选片段（不拼接）—— 与 <see cref="Apply"/> 共用同一判据。</summary>
    public static IReadOnlyList<string> Filter(IEnumerable<string?>? parts, JoinPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var list = new List<string>();
        if (parts is null) return list;

        foreach (var raw in parts)
        {
            var part = Normalize(raw, policy.Trim);
            if (policy.Empty == EmptyPolicy.DropEmpty && string.IsNullOrEmpty(part)) continue;
            if (policy.Empty == EmptyPolicy.DropBlank && string.IsNullOrWhiteSpace(part)) continue;
            list.Add(part);
        }
        return list;
    }

    private static string Normalize(string? raw, TrimPolicy trim) => trim switch
    {
        TrimPolicy.Trim => raw?.Trim() ?? string.Empty,
        TrimPolicy.BlankToEmpty => string.IsNullOrWhiteSpace(raw) ? string.Empty : raw!,
        _ => raw ?? string.Empty,
    };
}
