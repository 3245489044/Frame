#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Observe;
using Plate.HtmlViewer;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 观察 HTML 通道装饰器（连接层组合根）——把底层纯通道 <see cref="IHtmlChannel"/> 包装为
/// <see cref="IObservedHtmlChannel"/>，使宿主可经 Observe 收敛写（Converged/Degraded）投递 HTML。
/// 内部组合 <see cref="ObserveSessionManager"/> + <see cref="WebView2Transport"/>（本类真实归属 FrontEndDriven），
/// 其余纯渲染/封送面转发给底层通道。
/// </summary>
public sealed class ObservedHtmlChannel : IObservedHtmlChannel
{
    private readonly IHtmlChannel _inner;
    private readonly ObserveSessionManager _manager;

    public ObservedHtmlChannel(IHtmlChannel inner, TimeSpan? convergeTimeout = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _manager = new ObserveSessionManager(new WebView2Transport(inner, convergeTimeout));
    }

    public event EventHandler<HtmlMessageReceivedEventArgs>? MessageReceived
    {
        add => _inner.MessageReceived += value;
        remove => _inner.MessageReceived -= value;
    }

    public Task RenderHtmlAsync(string html, CancellationToken ct = default)
        => _inner.RenderHtmlAsync(html, ct);

    public Task<string> ExecuteAsync(string op, string name, string? value, CancellationToken ct = default)
        => _inner.ExecuteAsync(op, name, value, ct);

    public Task<string> ExecuteScriptAsync(string javaScript, CancellationToken ct = default)
        => _inner.ExecuteScriptAsync(javaScript, ct);

    public Task<bool> IsBridgeReadyAsync(CancellationToken ct = default)
        => _inner.IsBridgeReadyAsync(ct);

    public Task<bool> LoopbackProbeAsync(CancellationToken ct = default)
        => _inner.LoopbackProbeAsync(ct);

    public Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default)
        => _manager.WriteAsync("html:document", html, options, ct);

    public void Dispose() => _manager.Dispose();

    /// <summary>
    /// 把本观察通道适配为 plate 的 <see cref="HtmlWritePump"/>（HtmlBackendRuntime 写回端口）：
    /// 经收敛写投递并等待「可观测」收敛，把 ObserveOutcome 映射为 plate 本地 <see cref="HtmlWriteBackResult"/>，
    /// 使 plate 不再感知任何 Observe 类型。缺省 Barrier=Timeout / 5s 窗口（对齐原 plate 写回语义）。
    /// </summary>
    public HtmlWritePump ToWritePump(TimeSpan? window = null)
        => async (html, ct) =>
        {
            var outcome = await WriteAsync(html, new WriteOptions(
                BarrierMode.Timeout, window ?? TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
            var status = outcome.Result switch
            {
                ChangeResult.Committed => HtmlWriteBackStatus.Committed,
                ChangeResult.Degraded => HtmlWriteBackStatus.Degraded,
                _ => HtmlWriteBackStatus.Failed,
            };
            return new HtmlWriteBackResult(status, outcome.Window, outcome.HighestAck.ToString());
        };
}
