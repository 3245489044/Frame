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
/// <param name="DropBlankGroups">纯空白组是否丢弃（默认 false：只丢空串组）。</param>
/// <param name="FinalTrim">组文本是否整体裁剪（默认 false；Wire 等最终串形态用）。</param>
public sealed record ComposePolicy(
    IReadOnlyList<string> GroupOrder,
    Func<string, JoinPolicy>? JoinOf = null,
    bool EmitEmptyGroups = false,
    bool DropBlankGroups = false,
    bool FinalTrim = false)
{
    /// <summary>单组装配（全部段进同一组）。</summary>
    public static ComposePolicy SingleGroup(string group, JoinPolicy? join = null)
        => new(new[] { group }, _ => join ?? JoinPolicy.BlankLine);

    /// <summary>本组拼接策略。</summary>
    public JoinPolicy JoinFor(string group) => JoinOf?.Invoke(group) ?? JoinPolicy.BlankLine;
}
