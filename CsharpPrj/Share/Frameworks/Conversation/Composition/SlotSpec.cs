using System;
using System.Collections.Generic;

namespace Share.Composition;

/// <summary>
/// 槽位规格 —— 组装内核的一等概念，替代散落的「放哪 / 顺序 / 是否跳过 / 如何拼接」判断。
/// <see cref="When"/> 为激活条件（如 isFirstStep 才注入技能）；<see cref="Join"/> 为拼接策略
/// （空行 / 填槽 / 边分隔），缺省用 <see cref="KernelJoins.BlankLine"/>（空段过滤 + 空行分隔）。
/// </summary>
/// <param name="Name">槽位标识（与 <see cref="IComponent.Slot"/> 匹配）。</param>
/// <param name="When">激活条件（null = 恒激活）。</param>
/// <param name="Join">拼接策略（null = <see cref="KernelJoins.BlankLine"/>）。</param>
public sealed record SlotSpec(
    string Name,
    Func<IAssemblyContext, bool>? When = null,
    Func<IReadOnlyList<IComponent>, string>? Join = null)
{
    /// <summary>该槽在当前上下文是否激活（null 条件 → 恒激活）。</summary>
    public bool Active(IAssemblyContext ctx) => When is null || When(ctx);

    /// <summary>按本槽策略拼接候选部件（缺省空行分隔）。</summary>
    public string JoinOf(IReadOnlyList<IComponent> components)
        => (Join ?? KernelJoins.BlankLine)(components);
}
