using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;

namespace Plate.MarkdownViewer;

/// <summary>
/// WinForms 内嵌 Markdown 查看器：持有 Markdown 字符串 → <see cref="MarkdownHtmlRenderer"/> 渲染为 HTML →
/// WebView2.NavigateToString 显示。一次性 <see cref="WebView2.EnsureCoreWebView2Async"/>（需 Edge WebView2 运行时），
/// 之后可反复 <see cref="SetMarkdownAsync"/> 更新内容 / <see cref="ApplyFontSizeAsync"/> 调整字号。
/// 可置于任意 WinForms 容器（含 FrontEndDriven 的 ContainerView 子面板）。
/// </summary>
public sealed class MarkdownViewer : WebView2
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _ready;
    private string? _lastMarkdown;

    /// <summary>Markdown 文档基础字号（px）。默认 15（对齐 <see cref="MarkdownHtmlRenderer.DefaultCss"/>）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double FontSizePx { get; set; } = 15;

    /// <summary>渲染并显示 Markdown。可任意次数调用；首次会完成 WebView2 初始化（保持调用方 SynchronizationContext，UI 线程安全）。</summary>
    public async Task SetMarkdownAsync(string? markdown, CancellationToken ct = default)
    {
        _lastMarkdown = markdown;
        await EnsureReadyAsync(ct);
        if (CoreWebView2 is not null)
            CoreWebView2.NavigateToString(MarkdownHtmlRenderer.ToMarkdownHtml(markdown, FontSizePx));
    }

    /// <summary>仅更新字号并重渲染当前内容（若有）；无内容时仅记录字号，下次渲染生效。</summary>
    public async Task ApplyFontSizeAsync(double fontSizePx, CancellationToken ct = default)
    {
        if (fontSizePx > 0)
            FontSizePx = fontSizePx;
        if (_lastMarkdown is null)
            return;
        await EnsureReadyAsync(ct);
        if (CoreWebView2 is not null)
            CoreWebView2.NavigateToString(MarkdownHtmlRenderer.ToMarkdownHtml(_lastMarkdown, FontSizePx));
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        await _initLock.WaitAsync(ct);
        try
        {
            if (!_ready)
            {
                await EnsureCoreWebView2Async();
                _ready = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }
}
