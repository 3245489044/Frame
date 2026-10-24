#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>一个投喂组的装配结果（组标签 + 组文本 + 成员）。</summary>
/// <param name="Group">组标签（不透明，域侧解释）。</param>
/// <param name="Text">组内拼接结果。</param>
/// <param name="Members">参与拼接的段（含显式空段）。</param>
public sealed record GroupText(string Group, string Text, IReadOnlyList<TextBlock> Members);

/// <summary>逐段绑定 —— 键/组/序/状态/文本哈希/原因，供 UI ≡ 投喂 双向比对。</summary>
public sealed record TextBinding(
    TextKey Key,
    string Group,
    OrderKey Order,
    TextState State,
    string TextHash,
    string? Reason);

/// <summary>
/// 装配产物 —— 分组文本 + 逐段绑定 + 指纹。内核只产「组 → 文本」，不产消息角色。
/// </summary>
/// <param name="Groups">组结果（按 <see cref="ComposePolicy.GroupOrder"/> 排序）。</param>
/// <param name="Bindings">逐段绑定（覆盖全部段，含不适用/未知，供可见化）。</param>
/// <param name="Fingerprint">装配指纹（有序绑定 + 组文本的摘要）。</param>
public sealed record TextAssembly(
    IReadOnlyList<GroupText> Groups,
    IReadOnlyList<TextBinding> Bindings,
    string Fingerprint)
{
    /// <summary>空装配。</summary>
    public static TextAssembly Empty { get; } = new(
        Array.Empty<GroupText>(),
        Array.Empty<TextBinding>(),
        TextDigest.Hash(string.Empty));

    /// <summary>取组文本（不存在 → 空串）。</summary>
    public string TextOf(string group)
    {
        foreach (var g in Groups)
            if (string.Equals(g.Group, group, StringComparison.Ordinal)) return g.Text;
        return string.Empty;
    }
}
