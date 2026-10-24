#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>
/// 装配策略 —— 组的顺序 + 组级拼接策略 + 是否发射空组。
/// 内核只按「组标签」分组，不解释组语义（System/User 等角色由领域渲染适配器映射）。
/// </summary>
/// <param name="GroupOrder">组的输出顺序（声明序即输出序）。</param>
/// <param name="JoinOf">组级拼接策略（null → <see cref="JoinPolicy.BlankLine"/>）。</param>
/// <param name="EmitEmptyGroups">空组是否保留（默认丢弃）。</param>
public sealed record ComposePolicy(
    IReadOnlyList<string> GroupOrder,
    Func<string, JoinPolicy>? JoinOf = null,
    bool EmitEmptyGroups = false)
{
    /// <summary>单组装配（全部段进同一组）。</summary>
    public static ComposePolicy SingleGroup(string group, JoinPolicy? join = null)
        => new(new[] { group }, _ => join ?? JoinPolicy.BlankLine);

    /// <summary>本组拼接策略。</summary>
    public JoinPolicy JoinFor(string group) => JoinOf?.Invoke(group) ?? JoinPolicy.BlankLine;
}
