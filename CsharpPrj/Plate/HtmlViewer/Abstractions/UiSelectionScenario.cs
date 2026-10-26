#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>锚来源（回退链的每一格必须声明"这一格凭什么命中"）。</summary>
public enum UiAnchorSource
{
    /// <summary>本次回投的显式值。</summary>
    Incoming = 0,

    /// <summary>本 (作用域, 槽) 上次选中（粘滞）。</summary>
    Sticky = 1,

    /// <summary>跨作用域最近点选（导航恢复）。</summary>
    CrossScope = 2,

    /// <summary>领域声明的默认（如"当前历史首个任务"）。</summary>
    DeclaredDefault = 3,

    /// <summary>该作用域不适用该锚（不渲染）。</summary>
    NotApplicable = 4,
}

/// <summary>回退链的一格：作用域 × 槽 × 来源。</summary>
public readonly record struct UiAnchorStep(string Scope, string Slot, UiAnchorSource Source);

/// <summary>
/// 选择场景（唯一执行器）：把"读哪个槽、按什么顺序回退"从各调用点收敛为一条声明链。
/// <para>
/// 单一写入者的对偶：本类只负责**解析**，写入仍经 <see cref="UiSelectionStore"/>；
/// 命令层只声明链，不各自写"空 ⇒ 默认"。
/// </para>
/// </summary>
public sealed class UiSelectionScenario
{
    /// <summary>按声明链解析锚（首个命中即返回；全链未命中 ⇒ <see cref="UiAnchor.Unknown"/>）。</summary>
    /// <param name="store">选择态存储（Sticky 来源的唯一读面）。</param>
    /// <param name="chain">回退链（顺序即语义）。</param>
    /// <param name="provideIncoming">Incoming 来源提供者（未提供 ⇒ 该格跳过）。</param>
    /// <param name="provideCrossScope">CrossScope 来源提供者（未提供 ⇒ 该格跳过）。</param>
    /// <param name="provideDeclaredDefault">DeclaredDefault 来源提供者（未提供 ⇒ 该格跳过）。</param>
    public UiAnchor Resolve(
        UiSelectionStore store,
        IReadOnlyList<UiAnchorStep> chain,
        Func<UiAnchorStep, string?>? provideIncoming = null,
        Func<UiAnchorStep, string?>? provideCrossScope = null,
        Func<UiAnchorStep, string?>? provideDeclaredDefault = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(chain);

        foreach (var step in chain)
        {
            switch (step.Source)
            {
                case UiAnchorSource.Incoming:
                    if (NonEmpty(provideIncoming?.Invoke(step)) is { } incoming)
                        return UiAnchor.Known(incoming);
                    break;

                case UiAnchorSource.Sticky:
                    if (NonEmpty(store.Get(step.Scope, step.Slot)) is { } sticky)
                        return UiAnchor.Known(sticky);
                    break;

                case UiAnchorSource.CrossScope:
                    if (NonEmpty(provideCrossScope?.Invoke(step)) is { } cross)
                        return UiAnchor.Known(cross);
                    break;

                case UiAnchorSource.DeclaredDefault:
                    if (NonEmpty(provideDeclaredDefault?.Invoke(step)) is { } declared)
                        return UiAnchor.Known(declared);
                    break;

                case UiAnchorSource.NotApplicable:
                    return UiAnchor.NotApplicable;

                default:
                    throw new ArgumentOutOfRangeException(nameof(chain), step.Source,
                        "未登记的锚来源（回退链须穷举声明）。");
            }
        }

        return UiAnchor.Unknown;
    }

    /// <summary>当前值（只读；无值 ⇒ null，不伪造默认）。</summary>
    public string? Current(UiSelectionStore store, string? scope, string slot)
    {
        ArgumentNullException.ThrowIfNull(store);
        return NonEmpty(store.Get(scope, slot));
    }

    private static string? NonEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
