#nullable enable

using System;
using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>容器结构槽（框架词，零领域语义）：由容器键后缀 + 角色唯一判定。</summary>
public enum UiViewContainerSlot
{
    /// <summary>声明式内容容器（键即内容域，如 region-feed）。</summary>
    Content = 0,

    /// <summary>主区结构包裹（非容器，非写目标）。</summary>
    Main,

    Seg,
    Head,
    SubHead,
    Aside,
    Overlay,
    Composer,

    /// <summary>append-only 流容器（由流原语承载，不做区域写）。</summary>
    Stream,
}

/// <summary>
/// 容器槽分类器（框架件，单一实现）：「键后缀 → 结构槽」只在此处判定。
/// <para>消费者：壳渲染与区域节点装配（Composition 层）。新增结构槽/改前缀只改本类。</para>
/// </summary>
public static class UiViewContainerSlots
{
    /// <summary>结构槽词表（**后缀 ↔ 槽 的唯一表**）：正反映射均由此表派生，禁第二处枚举字面量。</summary>
    private static readonly (UiViewContainerSlot Slot, string Suffix)[] Vocabulary =
    {
        (UiViewContainerSlot.Main, "main"),
        (UiViewContainerSlot.Seg, "seg"),
        (UiViewContainerSlot.Head, "head"),
        (UiViewContainerSlot.SubHead, "subhead"),
        (UiViewContainerSlot.Aside, "aside"),
        (UiViewContainerSlot.Overlay, "overlay"),
        (UiViewContainerSlot.Composer, "composer"),
    };

    private static readonly Dictionary<string, UiViewContainerSlot> SlotBySuffix = BuildSlotBySuffix();
    private static readonly Dictionary<UiViewContainerSlot, string> SuffixBySlot = BuildSuffixBySlot();

    private static Dictionary<string, UiViewContainerSlot> BuildSlotBySuffix()
    {
        var map = new Dictionary<string, UiViewContainerSlot>(Vocabulary.Length, StringComparer.Ordinal);
        foreach (var (slot, suffix) in Vocabulary) map[suffix] = slot;
        return map;
    }

    private static Dictionary<UiViewContainerSlot, string> BuildSuffixBySlot()
    {
        var map = new Dictionary<UiViewContainerSlot, string>(Vocabulary.Length);
        foreach (var (slot, suffix) in Vocabulary) map[slot] = suffix;
        return map;
    }

    /// <summary>区域键 → 容器后缀（无前缀 ⇒ 原样返回）。</summary>
    public static string RegionSuffix(string? containerKey)
    {
        if (string.IsNullOrWhiteSpace(containerKey)) return string.Empty;
        var key = containerKey!.Trim();
        return key.StartsWith(UiViewNames.RegionNamePrefix, StringComparison.Ordinal)
            ? key.Substring(UiViewNames.RegionNamePrefix.Length)
            : key;
    }

    /// <summary>容器后缀 → 区域键（<see cref="RegionSuffix"/> 的逆；键构造唯一入口）。</summary>
    public static string RegionKey(string? suffix)
        => UiViewNames.RegionNamePrefix + (suffix ?? string.Empty).Trim();

    /// <summary>容器键 → 槽后缀（<see cref="RegionSuffix"/> 的语义别名）。</summary>
    public static string Suffix(string? containerKey) => RegionSuffix(containerKey);

    /// <summary>结构槽 → 容器后缀（词表反向唯一映射；非结构槽 ⇒ 空串）。</summary>
    public static string SuffixOf(UiViewContainerSlot slot)
        => SuffixBySlot.TryGetValue(slot, out var suffix) ? suffix : string.Empty;

    /// <summary>容器键（+ 角色）→ 结构槽。</summary>
    public static UiViewContainerSlot Classify(string? containerKey, bool isStream = false)
        => SlotBySuffix.TryGetValue(RegionSuffix(containerKey), out var slot)
            ? slot
            : isStream ? UiViewContainerSlot.Stream : UiViewContainerSlot.Content;

    /// <summary>容器声明 → 结构槽（流角色优先于键名）。</summary>
    public static UiViewContainerSlot Classify(UiViewContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.IsStream
            ? UiViewContainerSlot.Stream
            : Classify(container.Key);
    }

    /// <summary>是否为「壳位」（位于主区之外，与主区平级的独立槽）。</summary>
    public static bool IsShellSlot(UiViewContainerSlot slot)
        => slot is UiViewContainerSlot.Seg or UiViewContainerSlot.Head or UiViewContainerSlot.SubHead
            or UiViewContainerSlot.Aside or UiViewContainerSlot.Overlay;

    /// <summary>
    /// 该槽的取值是否来自壳槽（而非容器内容）。
    /// <para>与 <see cref="IsShellSlot"/> 正交：<c>composer</c> 位于主区内（不是壳位），
    /// 但取值仍由壳槽供给 —— 装配路径若按「位置」推断取值来源，就会分叉出空区域。</para>
    /// </summary>
    public static bool HasShellSource(UiViewContainerSlot slot)
        => IsShellSlot(slot) || slot == UiViewContainerSlot.Composer;
}
