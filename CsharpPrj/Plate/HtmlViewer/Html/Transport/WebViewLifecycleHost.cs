#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Plate.HtmlViewer;

namespace UiBackend.Html;

/// <summary>
/// WebView2 生命周期宿主（L0 控件适配层）——把「一次性初始化 + 主题/背景锚定 + 默认占位 + 内容/字号重渲染」
/// 从控件方法体收敛为统一宿主。文档壳/主题/字号决策经 <see cref="IWebViewContentProvider"/> 留在控件；
/// CoreWebView2 访问经 <see cref="IWebViewHost"/> 收敛。
/// <para>前置：调用须在 UI 线程（WebView2 仅限 UI 线程）；本类不做线程封送。</para>
/// </summary>
public sealed class WebViewLifecycleHost
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly IWebViewCore _host;
    private readonly IWebViewContentProvider _content;
    private readonly Action _ensureBridgeInstalled;
    private readonly DiagnosticPort _diagnostics = new();
    private bool _ready;

    /// <param name="host">控件宿主核心端口。</param>
    /// <param name="content">视图内容提供者。</param>
    /// <param name="ensureBridgeInstalled">就绪后绑定消息桥（幂等）——由消息传输组件提供；无桥查看器传空操作。</param>
    /// <param name="diagnostics">诊断出口（未接线为空操作）。</param>
    public WebViewLifecycleHost(
        IWebViewCore host,
        IWebViewContentProvider content,
        Action ensureBridgeInstalled,
        Action<string, string>? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _ensureBridgeInstalled = ensureBridgeInstalled ?? throw new ArgumentNullException(nameof(ensureBridgeInstalled));
        _diagnostics.Sink = diagnostics;
    }

    /// <summary>是否已完成一次性初始化。</summary>
    public bool IsReady => _ready;

    /// <summary>确保 WebView2 就绪（一次性初始化 + 主题锚定 + 默认占位）并绑定消息桥（首次 / 重建后重订阅）。</summary>
    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_ready)
            {
                await _host.EnsureCoreAsync().ConfigureAwait(false);
                var theme = _content.ResolveTheme();
                // 深浅色显式锚定：按 Mode 锚定 WebView2 配色与默认背景，避免「随系统猜测而黑」/ 周边底色不一致。
                if (_host.Core?.Profile is not null)
                    _host.Core.Profile.PreferredColorScheme = _content.Mode.Map();
                var bg = HtmlThemeBackground.Resolve(theme.ToCss(), _content.Mode);
                if (bg.HasValue)
                    _host.DefaultBackgroundColor = bg.Value;
                _ready = true;
                // 无内容时显示默认占位（仅在从未渲染时；RenderAsync 会先设 LastBodyHtml 再进来）。
                if (_host.Core is not null && _content.LastBodyHtml is null && !string.IsNullOrWhiteSpace(_content.DefaultBodyHtml))
                    _host.NavigateToHtmlDocument(_content.RenderDocument(_content.DefaultBodyHtml));
            }
            // 消息桥订阅：首次就绪 + CoreWebView2 重建后重新绑定（幂等：同实例引用则跳过）。
            _ensureBridgeInstalled();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>渲染并显示 body（先记录最近内容，再确保就绪，最后以完整文档导航）。</summary>
    public async Task RenderAsync(string? bodyHtml, CancellationToken ct)
    {
        _content.LastBodyHtml = bodyHtml;
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        if (_host.Core is not null)
            _host.NavigateToHtmlDocument(_content.RenderDocument(bodyHtml));
    }

    /// <summary>仅更新字号并重渲染当前内容（若有）；无内容时仅记录字号，下次渲染生效。</summary>
    public async Task ApplyFontSizeAsync(double fontSizePx, CancellationToken ct)
    {
        if (fontSizePx > 0)
            _content.FontSizePx = fontSizePx;
        if (_content.LastBodyHtml is null)
            return;
        await EnsureReadyAsync(ct).ConfigureAwait(false);
        if (_host.Core is not null)
            _host.NavigateToHtmlDocument(_content.RenderDocument(_content.LastBodyHtml));
    }
}
