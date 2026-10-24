#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>
/// 装配唯一实现 —— 计划 × 策略 → 装配产物。
/// <para>
/// 只做「按组过滤 → 组内按序拼接 → 逐段留痕」，不判空、不 Trim、不改序：
/// 判空/分隔/裁剪全部由 <see cref="JoinPolicy"/> 承载，顺序全部由 <see cref="OrderKey"/> 承载。
/// </para>
/// </summary>
public static class TextCompose
{
    /// <summary>装配。</summary>
    public static TextAssembly Compose(TextPlan plan, ComposePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(policy);

        var bindings = new List<TextBinding>(plan.Blocks.Count);
        foreach (var block in plan.Blocks)
        {
            bindings.Add(new TextBinding(
                block.Key, block.Group, block.Order, block.State,
                TextDigest.Hash(block.Text), block.Reason));
        }

        var groups = new List<GroupText>(policy.GroupOrder.Count);
        // 计划已按 (Order, 声明序) 稳定排序 ⇒ 组内过滤后天然保序，无需二次排序。
        foreach (var group in policy.GroupOrder)
        {
            var members = plan.Blocks
                .Where(b => b.IsUsable && string.Equals(b.Group, group, StringComparison.Ordinal))
                .ToArray();
            var text = TextJoin.Apply(members.Select(static b => b.Text), policy.JoinFor(group));

            if (text.Length == 0 && !policy.EmitEmptyGroups) continue;
            groups.Add(new GroupText(group, text, members));
        }

        var lines = new List<string>(bindings.Count + groups.Count);
        foreach (var binding in bindings)
            lines.Add($"{binding.Group}|{binding.Key.Value}|{binding.State}|{binding.TextHash}|{binding.Order.Value}");
        foreach (var group in groups)
            lines.Add($"@{group.Group}|{TextDigest.Hash(group.Text)}");

        return new TextAssembly(groups, bindings, TextDigest.HashLines(lines));
    }
}
