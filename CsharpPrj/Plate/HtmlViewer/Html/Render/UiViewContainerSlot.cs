#nullable enable

using System;
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
/// <para>消费者：壳渲染（<see cref="HtmlShellProjector.RenderContainerShell"/>）与区域节点装配
/// （<see cref="HtmlShellProjector.ComposeContents"/>）。新增结构槽/改前缀只改本类。</para>
/// </summary>
public static class UiViewContainerSlots
{
    /// <summary>容器键前缀（与 <see cref="HtmlShellProjector.RegionNamePrefix"/> 同值）。</summary>
    public const string Prefix = HtmlShellProjector.RegionNamePrefix;

    /// <summary>容器键 → 槽后缀（无前缀 ⇒ 原样返回）。</summary>
    public static string Suffix(string? containerKey)
    {
        if (string.IsNullOrWhiteSpace(containerKey)) return string.Empty;
        var key = containerKey!.Trim();
        return key.StartsWith(Prefix, StringComparison.Ordinal) ? key.Substring(Prefix.Length) : key;
    }

    /// <summary>容器键（+ 角色）→ 结构槽。</summary>
    public static UiViewContainerSlot Classify(string? containerKey, bool isStream = false)
    {
        var suffix = Suffix(containerKey);
        return suffix switch
        {
            "main" => UiViewContainerSlot.Main,
            "seg" => UiViewContainerSlot.Seg,
            "head" => UiViewContainerSlot.Head,
            "subhead" => UiViewContainerSlot.SubHead,
            "aside" => UiViewContainerSlot.Aside,
            "overlay" => UiViewContainerSlot.Overlay,
            "composer" => UiViewContainerSlot.Composer,
            _ => isStream ? UiViewContainerSlot.Stream : UiViewContainerSlot.Content,
        };
    }

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
}
