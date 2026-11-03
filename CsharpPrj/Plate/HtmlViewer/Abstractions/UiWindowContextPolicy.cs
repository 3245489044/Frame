#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Abstractions;

/// <summary>
/// 窗口上下文取形声明（机制层，**载荷无关**）：条数 + 阶段/角色排除词表。
/// <para>
/// 声明来源由领域注入（本层不读配置文件、不引用领域消息类型）；空词表 + Take=0 ⇒ 不过滤（
/// 禁静默过滤：空声明必须原样返回）。
/// </para>
/// </summary>
public sealed record UiWindowContextSpec(
    int Take,
    IReadOnlySet<string> ExcludedStages,
    IReadOnlySet<string> ExcludedRoles)
{
    /// <summary>空声明（不过滤、不限条数）。</summary>
    public static UiWindowContextSpec Empty { get; } = new(
        0,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// 窗口上下文取形（通用件，唯一实现）—— 可见谓词 + 源序保留 + 末 N 条切片。
/// <para>
/// 语义：<see cref="UiWindowContextSpec.ExcludedRoles"/> 命中 ⇒ 不可见；
/// <see cref="UiWindowContextSpec.ExcludedStages"/> 命中（精确词或 <c>token:</c>/<c>token/</c>/<c>token.</c>
/// 子阶段形态）⇒ 不可见；其余保留。切片在过滤**之后**做一次（禁二次裁剪）。
/// </para>
/// </summary>
public static class UiWindowContextPolicy
{
    /// <summary>按声明过滤并取末 N 条（源序保留；空声明/空输入 ⇒ 不伪造）。</summary>
    public static IReadOnlyList<T> Apply<T>(
        IReadOnlyList<T> items,
        UiWindowContextSpec spec,
        Func<T, string?> stageOf,
        Func<T, string?> roleOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(stageOf);
        ArgumentNullException.ThrowIfNull(roleOf);
        if (items.Count == 0) return Array.Empty<T>();

        if (spec.ExcludedStages.Count == 0 && spec.ExcludedRoles.Count == 0)
            return Trim(items, spec.Take);

        var visible = new List<T>(items.Count);
        foreach (var item in items)
        {
            if (IsVisible(spec, stageOf(item), roleOf(item))) visible.Add(item);
        }

        return Trim(visible, spec.Take);
    }

    /// <summary>单条可见判定（空 stage/role ⇒ 不因该维度被排除）。</summary>
    public static bool IsVisible(UiWindowContextSpec spec, string? stage, string? role)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!string.IsNullOrWhiteSpace(role) && spec.ExcludedRoles.Contains(role.Trim())) return false;
        if (string.IsNullOrWhiteSpace(stage)) return true;

        var normalized = stage.Trim();
        foreach (var token in spec.ExcludedStages)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var word = token.Trim();
            // 精确词或「词:子阶段」才算内部过程：子串匹配会把合法阶段误杀。
            if (normalized.Equals(word, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(word + ":", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(word + "/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(word + ".", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<T> Trim<T>(IReadOnlyList<T> items, int take)
    {
        if (take <= 0 || items.Count <= take) return items;
        var result = new List<T>(take);
        for (var index = items.Count - take; index < items.Count; index++) result.Add(items[index]);
        return result;
    }
}
