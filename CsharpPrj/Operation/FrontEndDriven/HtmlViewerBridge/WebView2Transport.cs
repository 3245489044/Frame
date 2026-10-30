#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// WebView2 传输适配器（HTML/UI 场景落地）：把 <see cref="IHtmlChannel"/>（纯渲染/封送通道）适配为
/// Modules.Observe 的 ITransport，使 HTML 写路径可经 Observe 协调器获得「写-可观测」收敛保证。
/// 收敛证据：<see cref="Modules.Observe.EvidenceStrategy.Probe"/>——渲染投递后后台轮询
/// <see cref="IHtmlChannel.IsBridgeReadyAsync"/> 确认桥就绪（逼近式收敛），就绪即回发 Converged，
/// 界内未就绪则由协调器超时降级。
/// <para>历史归属：本文件原置于 Plate.HtmlViewer（命名空间伪装 Modules.Observe.Transports），
/// 属 Observe×plate 的桥（跨层胶水），现迁入 FrontEndDriven 连接层（真实组合根宿主层）。</para>
/// </summary>
public sealed class WebView2Transport : Modules.Observe.ITransport
{
    private readonly Modules.Observe.ProbeTransport _inner;

    public WebView2Transport(IHtmlChannel channel, TimeSpan? convergeTimeout = null)
    {
        if (channel is null) throw new ArgumentNullException(nameof(channel));
        // 收敛语义唯一实现 = Modules.Observe.ProbeTransport（投递 → Applied → 轮询就绪 → Converged）。
        _inner = new Modules.Observe.ProbeTransport(
            (command, ct) => channel.RenderHtmlAsync(command.PayloadObject?.ToString() ?? string.Empty, ct),
            ct => channel.IsBridgeReadyAsync(ct),
            convergeTimeout,
            TimeSpan.FromMilliseconds(50),
            "webview2 dom bridge ready");
    }

    public Modules.Observe.EvidenceStrategy EvidenceStrategy => _inner.EvidenceStrategy;

    public IDisposable Subscribe(Func<Modules.Observe.Message, CancellationToken, Task> handler)
        => _inner.Subscribe(handler);


    public Task SendAsync(Modules.Observe.Message message, CancellationToken ct = default)
        => _inner.SendAsync(message, ct);

    public void Dispose() => _inner.Dispose();
}

