#nullable enable

using System.Collections.Generic;
using System.Net;

namespace UiBackend.Html;

/// <summary>通用浮层（overlay）HTML 构建器选项 —— 类名/关闭语义/尺寸均参数化，不硬编码领域命名。</summary>
public sealed record HtmlOverlayOptions
{
    /// <summary>关闭按钮的 data-cmd（宿主消息桥凭此收起浮层）；null = 不渲染关闭按钮。</summary>
    public string? CloseCmd { get; init; }

    /// <summary>关闭按钮附加 data-* 属性（键→值，值经 HTML 编码）；如 { ["session"] = taskKey }。可空。</summary>
    public IReadOnlyDictionary<string, string>? CloseData { get; init; }

    /// <summary>浮层 CSS 类前缀（生成 {CssClass} / {CssClass}-title / {CssClass}-body 类与 DOM 结构）。默认 floating-window。</summary>
    public string CssClass { get; init; } = "floating-window";

    /// <summary>根元素 data-name 值（供宿主脚本定位）。默认 floatingWindow。</summary>
    public string DataName { get; init; } = "floatingWindow";

    /// <summary>浮层宽度 CSS。默认 min(46%,520px)。</summary>
    public string WidthCss { get; init; } = "min(46%,520px)";

    /// <summary>浮层高度 CSS。默认 min(78%,480px)。</summary>
    public string HeightCss { get; init; } = "min(78%,480px)";
}

/// <summary>
/// 通用浮层（overlay）HTML 构建器 —— 把一段 body 片段包装为 position:fixed 悬浮于宿主页之上的浮层，
/// 附标题栏与可选的关闭按钮（data-cmd 经宿主消息桥 postMessage 收起）。纯函数、零宿主依赖：
/// 数据源（body）由宿主注入，本组件只负责浮层壳的 HTML 结构 + 样式；类名/data-name/关闭语义均经
/// <see cref="HtmlOverlayOptions"/> 参数化（不硬编码领域命名）。
/// [下沉自 Agent.Launcher ConversationViewService.BuildTaskWindowHtmlAsync 的 task-window 内嵌实现，已通用化]
/// </summary>
public static class HtmlOverlay
{
    /// <summary>构建浮层 HTML 片段（含内嵌样式）。<paramref name="bodyHtml"/> 已由生产端渲染器完成文本 encode（本方法不再转义 body）。</summary>
    /// <param name="bodyHtml">浮层内容 body 片段（已 encode）。</param>
    /// <param name="title">标题栏文本（本方法负责 encode）。</param>
    /// <param name="options">浮层选项（类名/关闭语义/尺寸）；缺省浮层默认值。</param>
    public static string Build(string? bodyHtml, string title, HtmlOverlayOptions? options = null)
    {
        var o = options ?? new HtmlOverlayOptions();
        var cls = string.IsNullOrWhiteSpace(o.CssClass) ? "floating-window" : o.CssClass!;
        var name = string.IsNullOrWhiteSpace(o.DataName) ? "floatingWindow" : o.DataName!;
        var safeTitle = WebUtility.HtmlEncode(title ?? string.Empty);
        var closeBtn = CloseButtonHtml(o);
        return
            "<style>" + OverlayCss(cls, o.WidthCss, o.HeightCss) + "</style>"
            + "<div class=\"" + cls + "\" data-name=\"" + name + "\">"
            + "<div class=\"" + cls + "-title\"><span>" + safeTitle + "</span>"
            + closeBtn + "</div>"
            + "<div class=\"" + cls + "-body\">" + (bodyHtml ?? string.Empty) + "</div></div>";
    }

    /// <summary>关闭按钮 HTML；未配置 CloseCmd 返回空串（无关闭按钮）。</summary>
    private static string CloseButtonHtml(HtmlOverlayOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.CloseCmd)) return string.Empty;
        var sb = new System.Text.StringBuilder("<button type=\"button\" class=\"ctrl-btn\" data-cmd=\"")
            .Append(o.CloseCmd).Append('"');
        if (o.CloseData is not null)
            foreach (var (k, v) in o.CloseData)
                if (!string.IsNullOrWhiteSpace(k))
                    sb.Append(" data-").Append(k).Append("=\"").Append(WebUtility.HtmlEncode(v ?? string.Empty)).Append('"');
        sb.Append(" title=\"关闭\">×</button>");
        return sb.ToString();
    }

    /// <summary>浮层 CSS（position:fixed 悬浮于右上，受宿主主题变量 --bg/--border/--muted/--agent-bg 控制）。</summary>
    private static string OverlayCss(string cls, string widthCss, string heightCss)
        => "." + cls + "{position:fixed;top:34px;right:14px;width:" + (widthCss ?? "min(46%,520px)") + ";height:" + (heightCss ?? "min(78%,480px)") + ";"
            + "background:var(--bg);border:1px solid var(--border);border-radius:10px;box-shadow:0 8px 28px rgba(0,0,0,.2);"
            + "display:flex;flex-direction:column;overflow:hidden;z-index:20}"
            + "." + cls + "-title{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:6px 10px;"
            + "font-size:12px;color:var(--muted);font-weight:600;border-bottom:1px solid var(--border);background:var(--agent-bg)}"
            + "." + cls + "-body{flex:1;overflow:auto;padding:8px 10px}";
}
