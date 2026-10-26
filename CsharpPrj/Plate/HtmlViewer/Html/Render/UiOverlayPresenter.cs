#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>浮层锚定方式（安全区由布局声明注入，禁领域硬编码像素）。</summary>
public enum OverlayAnchor
{
    /// <summary>视口固定（不受宿主区域约束）。</summary>
    Viewport = 0,

    /// <summary>安全区（从左边界起算，不得覆盖宿主声明的保留区）。</summary>
    SafeArea = 1,
}

/// <summary>
/// 浮层装配声明（标题/关闭语义/类名/尺寸/锚定/空态策略）——
/// 三个浮层只声明数据，不再各写一份 <see cref="HtmlOverlayOptions"/>。
/// </summary>
public sealed record OverlaySpec(
    string Title,
    string? CloseCmd,
    IReadOnlyDictionary<string, string>? CloseData,
    string CssClass,
    string DataName,
    string? WidthCss,
    string? HeightCss,
    OverlayAnchor Anchor,
    bool SuppressWhenEmpty);

/// <summary>
/// 浮层装配器（唯一实现）：声明 → <see cref="HtmlOverlayOptions"/>（安全区单源）+ 空态不收口。
/// <para>只做装配，不做 IO/宿主交互（受控态仍由宿主承载）。</para>
/// </summary>
public static class UiOverlayPresenter
{
    /// <summary>安全左边界缺省值（布局声明缺省：.shell-aside 176 + gap 14 + 4）。</summary>
    public const string DefaultSafeLeftCss = "194px";

    /// <summary>声明 → 通用浮层选项（Anchor=SafeArea ⇒ 注入左边界）。</summary>
    public static HtmlOverlayOptions ToOptions(OverlaySpec spec, string? safeLeftCss = null)
    {
        var left = spec.Anchor == OverlayAnchor.SafeArea
            ? (string.IsNullOrWhiteSpace(safeLeftCss) ? DefaultSafeLeftCss : safeLeftCss)
            : null;
        return new HtmlOverlayOptions
        {
            CloseCmd = spec.CloseCmd,
            CloseData = spec.CloseData,
            CssClass = spec.CssClass,
            DataName = spec.DataName,
            WidthCss = spec.WidthCss ?? new HtmlOverlayOptions().WidthCss,
            HeightCss = spec.HeightCss ?? new HtmlOverlayOptions().HeightCss,
            LeftCss = left,
        };
    }

    /// <summary>
    /// 装配浮层 HTML；<see cref="OverlaySpec.SuppressWhenEmpty"/>=true 且 body 为空 ⇒ null（不弹模态）。
    /// </summary>
    public static string? Build(string? bodyHtml, OverlaySpec spec, string? safeLeftCss = null)
    {
        if (spec.SuppressWhenEmpty && string.IsNullOrWhiteSpace(bodyHtml)) return null;
        return HtmlOverlay.Build(bodyHtml, spec.Title, ToOptions(spec, safeLeftCss));
    }
}
