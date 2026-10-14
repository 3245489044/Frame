#nullable enable

using Plate.HtmlViewer;

namespace UiBackend.Html;

/// <summary>
/// 视图内容提供者（L0 控件适配层）——生命周期宿主据此取「当前主题 / 字号 / 默认占位 / 待渲染 body」并产出完整文档，
/// 使文档壳/主题/字号的决策留在控件（保持既有公共 API），而导航与初始化时序收敛到 <see cref="WebViewLifecycleHost"/>。
/// 由控件宿主（<c>Plate.HtmlViewer.HtmlViewer</c>）实现。
/// </summary>
public interface IWebViewContentProvider
{
    /// <summary>当前生效主题文档（控件级主题优先，否则经主题提供者解析）。</summary>
    ThemeDocument ResolveTheme();

    /// <summary>深浅色模式（决定 WebView2 配色锚定与文档 color-scheme）。</summary>
    ThemeMode Mode { get; }

    /// <summary>文档基础字号（px）。</summary>
    double FontSizePx { get; set; }

    /// <summary>无内容时的默认占位 body（null/空白 = 关闭默认显示）。</summary>
    string? DefaultBodyHtml { get; }

    /// <summary>最近一次渲染的 body（null = 从未渲染；供字号变更重渲染/默认占位判定）。</summary>
    string? LastBodyHtml { get; set; }

    /// <summary>body 片段 → 完整 HTML 文档（含页壳/主题/字号）。</summary>
    string RenderDocument(string? bodyHtml);
}
