using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Plate.HtmlViewer;
using UiBackend.Html;

namespace Plate.MarkdownViewer;

/// <summary>
/// WinForms 内嵌 Markdown 查看器：持有 Markdown 字符串 → <see cref="MarkdownHtmlRenderer"/> 渲染为 HTML →
/// WebView2.NavigateToString 显示。一次性 <see cref="WebView2.EnsureCoreWebView2Async"/>（需 Edge WebView2 运行时），
/// 之后可反复 <see cref="SetMarkdownAsync"/> 更新内容 / <see cref="ApplyFontSizeAsync"/> 调整字号。
/// 可置于任意 WinForms 容器（含 FrontEndDriven 的 ContainerView 子面板）。
/// </summary>
/// <remarks>
/// 本控件复用通用控件适配层框架（<c>UiBackend.Html</c>）：WebView2 初始化/主题锚定经 <see cref="WebViewLifecycleHost"/>，
/// UI 线程封送经 <see cref="UiThreadMarshaler"/>（消除与 <c>HtmlViewer</c> 的重复实现）；本类只实现端口
/// （<see cref="IWebViewCore"/> / <see cref="IWebViewContentProvider"/> / <see cref="IUiThreadAffine"/>）并转发。
/// </remarks>
public sealed class MarkdownViewer : WebView2, IUiThreadAffine, IWebViewCore, IWebViewContentProvider
{
    private readonly UiThreadMarshaler _marshaler;
    private readonly WebViewLifecycleHost _lifecycle;
    private string? _lastMarkdown;

    /// <summary>Markdown 文档基础字号（px）。默认 15（对齐 <see cref="MarkdownHtmlRenderer.DefaultCss"/>）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double FontSizePx { get; set; } = 15;

    public MarkdownViewer()
    {
        // UI 线程封送器在构造线程捕获 SynchronizationContext；桥安装为无操作（Markdown 无交互消息桥）。
        _marshaler = new UiThreadMarshaler(this);
        _lifecycle = new WebViewLifecycleHost(this, this, () => { });
    }

    /// <summary>渲染并显示 Markdown。可任意次数调用；首次会完成 WebView2 初始化（保持调用方 SynchronizationContext，UI 线程安全）。</summary>
    public Task SetMarkdownAsync(string? markdown, CancellationToken ct = default)
        => _marshaler.RunAsync(c => new ValueTask(_lifecycle.RenderAsync(markdown, c)), ct);

    /// <summary>仅更新字号并重渲染当前内容（若有）；无内容时仅记录字号，下次渲染生效。</summary>
    public Task ApplyFontSizeAsync(double fontSizePx, CancellationToken ct = default)
        => _marshaler.RunAsync(c => new ValueTask(_lifecycle.ApplyFontSizeAsync(fontSizePx, c)), ct);

    // ── 端口实现（由控件适配层组件消费）──

    Task IUiThreadAffine.InvokeOnUiAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
        => InvokeAsync(action, ct);

    Task<T> IUiThreadAffine.InvokeOnUiAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
        => InvokeAsync(action, ct);

    CoreWebView2? IWebViewCore.Core => CoreWebView2;

    Task IWebViewCore.EnsureCoreAsync() => EnsureCoreWebView2Async();

    void IWebViewCore.NavigateToHtmlDocument(string html) => CoreWebView2?.NavigateToString(html);

    System.Drawing.Color IWebViewCore.DefaultBackgroundColor
    {
        get => DefaultBackgroundColor;
        set => DefaultBackgroundColor = value;
    }

    // Markdown 查看器无主题系统：空主题（背景锚定回退默认）+ System 配色（随系统）。
    ThemeDocument IWebViewContentProvider.ResolveTheme() => ThemeDocument.Empty;

    ThemeMode IWebViewContentProvider.Mode => ThemeMode.System;

    double IWebViewContentProvider.FontSizePx
    {
        get => FontSizePx;
        set => FontSizePx = value;
    }

    string? IWebViewContentProvider.DefaultBodyHtml => null;

    string? IWebViewContentProvider.LastBodyHtml
    {
        get => _lastMarkdown;
        set => _lastMarkdown = value;
    }

    string IWebViewContentProvider.RenderDocument(string? bodyHtml)
        => MarkdownHtmlRenderer.ToMarkdownHtml(bodyHtml, FontSizePx);
}
