#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 通用页面区域布局协议 —— 把整页壳拆为固定区域键集合（选区/标题/副标题/左区/主区子容器/浮层），
/// 整页渲染与区域渲染共用同一分区产物（避免两处各拼一份区域字典）。
/// <para>区域键与 <see cref="HtmlShellProjector.RegionNamePrefix"/> 对齐，可直接作为区域写回目标名。</para>
/// <para>分层写回（B3）：主区 <c>region-main</c> 仅作壳（首挂载创建，稳态不写），
/// 其子容器 <c>region-nodes</c> / <c>region-bubbles</c> / <c>region-composer</c> 才是稳态写目标；
/// 气泡段以 <see cref="StreamSectionPrefix"/> 前缀的段键参与差分，由流原语定点更新/追加。</para>
/// </summary>
public static class HtmlPageRegionLayout
{
    /// <summary>流段键前缀（气泡段：<c>bubbles:r{round}</c>）：差分键，不是 DOM 名。</summary>
    public const string StreamSectionPrefix = "bubbles:";

    /// <summary>区域键（顺序稳定；写回目标名 = <see cref="HtmlShellProjector.RegionNamePrefix"/> + Key）。
    /// <c>main</c> 不计入：它是壳容器（首挂载即固定），稳态写只落子容器。</summary>
    public static readonly IReadOnlyList<string> RegionKeys = new[] { "seg", "head", "subhead", "aside", "nodes", "composer", "overlay" };

    /// <summary>页面分区产物（各区域 HTML + 气泡段 + 刷新属性）。</summary>
    public sealed record Parts(
        string Selector,
        string Title,
        string Subtitle,
        string Tree,
        string Nodes,
        IReadOnlyList<(string Key, string Html)> Bubbles,
        string Composer = "",
        string Overlay = "",
        string PollAttrs = "");

    /// <summary>分区产物 → 区域片段字典（整页与区域路径共用同一口径）；气泡段按键逐个参与差分。</summary>
    public static IReadOnlyDictionary<string, string> ToRegions(Parts parts)
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seg"] = parts.Selector,
            ["head"] = parts.Title,
            ["subhead"] = parts.Subtitle,
            ["aside"] = parts.Tree,
            ["nodes"] = parts.Nodes,
            ["composer"] = parts.Composer,
            ["overlay"] = parts.Overlay,
        };
        if (parts.Bubbles is { Count: > 0 } sections)
        {
            foreach (var section in sections)
                regions[StreamSectionPrefix + section.Key] = section.Html;
        }

        return regions;
    }
}
