#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 通用页面区域布局协议 —— 把整页壳拆为固定区域键集合（选区/标题/副标题/左区/主区/浮层），
/// 整页渲染与区域渲染共用同一分区产物（避免两处各拼一份区域字典）。
/// <para>区域键与 <see cref="HtmlShellProjector.RegionNamePrefix"/> 对齐，可直接作为区域写回目标名。</para>
/// </summary>
public static class HtmlPageRegionLayout
{
    /// <summary>区域键（顺序稳定；写回目标名 = <see cref="HtmlShellProjector.RegionNamePrefix"/> + Key）。</summary>
    public static readonly IReadOnlyList<string> RegionKeys = new[] { "seg", "head", "subhead", "aside", "main", "overlay" };

    /// <summary>页面分区产物（各区域 HTML + 刷新属性）。</summary>
    public sealed record Parts(
        string Selector,
        string Title,
        string Subtitle,
        string Tree,
        string Main,
        string Overlay = "",
        string PollAttrs = "");

    /// <summary>分区产物 → 区域片段字典（整页与区域路径共用同一口径）。</summary>
    public static IReadOnlyDictionary<string, string> ToRegions(Parts parts)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seg"] = parts.Selector,
            ["head"] = parts.Title,
            ["subhead"] = parts.Subtitle,
            ["aside"] = parts.Tree,
            ["main"] = parts.Main,
            ["overlay"] = parts.Overlay,
        };
}
