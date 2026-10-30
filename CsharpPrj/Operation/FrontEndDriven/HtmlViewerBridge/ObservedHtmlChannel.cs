#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Observe;
using Plate.HtmlViewer;
using UiBackend.Abstractions;
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
    private readonly UiWriteBarrierPolicy _barrierPolicy;

    public ObservedHtmlChannel(
        IHtmlChannel inner,
        TimeSpan? convergeTimeout = null,
        UiWriteBarrierPolicy? barrierPolicy = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _manager = new ObserveSessionManager(new WebView2Transport(inner, convergeTimeout));
        _barrierPolicy = barrierPolicy ?? UiWriteBarrierPolicy.Default;
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

    /// <summary>宿主 → 页面反向推送（透传底层通道；收敛写仅覆盖渲染面）。</summary>
    public Task<bool> PostToPageAsync(string json, CancellationToken ct = default)
        => _inner.PostToPageAsync(json, ct);

    public Task<bool> LoopbackProbeAsync(CancellationToken ct = default)
        => _inner.LoopbackProbeAsync(ct);

    public Task<ObserveOutcome> WriteAsync(string html, WriteOptions? options = null, CancellationToken ct = default)
        => _manager.WriteAsync("html:document", html, options, ct);

    public void Dispose() => _manager.Dispose();

    /// <summary>
    /// 把本观察通道适配为 plate 的 <see cref="HtmlWritePump"/>（HtmlBackendRuntime 写回端口）：
    /// 经收敛写投递并等待「可观测」收敛，把 ObserveOutcome 映射为 plate 本地 <see cref="HtmlWriteBackResult"/>，
    /// 使 plate 不再感知任何 Observe 类型。缺省窗口 = Refresh 250ms / Mount 5s（对齐原 plate 写回语义）。
    /// </summary>
    public HtmlWritePump ToWritePump(TimeSpan? window = null)
        => window is null
            ? ToWritePump(_barrierPolicy)
            : ToWritePump(new UiWriteBarrierPolicy(RefreshWindow: window, MountWindow: window));

    /// <summary>按来源分档窗口（B4-5）：交互/流 ⇒ 不等待屏障（FireAndForget）；刷新 ⇒ 窄窗（缺省 250ms）；挂载 ⇒ 5s。</summary>
    public HtmlWritePump ToWritePump(Func<UiWriteSource, TimeSpan?>? windowSelector)
        => ToWritePump(new UiWriteBarrierPolicy(WindowSelector: windowSelector));

    /// <summary>按声明策略计算屏障选项（策略单源 = <see cref="UiWriteBarrierPolicy"/>）。</summary>
    public HtmlWritePump ToWritePump(UiWriteBarrierPolicy policy)
        => async (html, source, ct) =>
        {
            var options = policy.For(source);
            var outcome = await WriteAsync(html, options, ct).ConfigureAwait(false);
            var status = outcome.Result switch
            {
                ChangeResult.Committed => HtmlWriteBackStatus.Committed,
                ChangeResult.Degraded => HtmlWriteBackStatus.Degraded,
                _ => HtmlWriteBackStatus.Failed,
            };
            return new HtmlWriteBackResult(status, outcome.Window, outcome.HighestAck.ToString());
        };
}
