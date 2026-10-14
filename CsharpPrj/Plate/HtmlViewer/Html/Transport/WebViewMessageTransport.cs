#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace UiBackend.Html;

/// <summary>
/// WebView2 消息传输（L0 控件适配层）——把「消息桥订阅（含 CoreWebView2 重建后重订阅）+ 导航后点击桥注入 +
/// 写回后就绪收敛」从控件方法体收敛为统一组件。
/// <para>订阅按 CoreWebView2 实例引用幂等：仅当引用变化（首次就绪 / 控件重建）时才（重新）绑定，防重复订阅。</para>
/// </summary>
public sealed class WebViewMessageTransport
{
    /// <summary>写回收敛等待点击桥注入的上限（毫秒）。</summary>
    private const int ConvergeTimeoutMs = 2500;

    private readonly IWebViewHost _host;
    private readonly HtmlScriptHost _bridgeHost;
    private readonly UiThreadMarshaler _marshaler;
    private readonly Action<string> _onMessageJson;
    private readonly DiagnosticPort _diagnostics = new();
    private CoreWebView2? _subscribedCore;
    private TaskCompletionSource<bool>? _navBridgeReadyTcs;

    /// <param name="host">控件宿主端口。</param>
    /// <param name="bridgeHost">文档脚本生命周期宿主（导航后补注入点击桥）。</param>
    /// <param name="marshaler">UI 线程封送器（收敛查询封送）。</param>
    /// <param name="onMessageJson">收到 postMessage（原始 JSON；已 marshal 回 UI 线程）的回调。</param>
    /// <param name="diagnostics">诊断出口（未接线为空操作）。</param>
    public WebViewMessageTransport(
        IWebViewHost host,
        HtmlScriptHost bridgeHost,
        UiThreadMarshaler marshaler,
        Action<string> onMessageJson,
        Action<string, string>? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _bridgeHost = bridgeHost ?? throw new ArgumentNullException(nameof(bridgeHost));
        _marshaler = marshaler ?? throw new ArgumentNullException(nameof(marshaler));
        _onMessageJson = onMessageJson ?? throw new ArgumentNullException(nameof(onMessageJson));
        _diagnostics.Sink = diagnostics;
    }

    /// <summary>绑定消息桥（订阅 WebMessageReceived + NavigationCompleted）。幂等：同 CoreWebView2 实例重复调用跳过。</summary>
    public void EnsureInstalled()
    {
        var core = _host.Core;
        if (core is null) return;
        if (ReferenceEquals(_subscribedCore, core)) return;
        var wasBound = _subscribedCore is not null;

        core.WebMessageReceived += OnWebMessageReceived;
        // 桥脚本仅在「活 document」上注入：WebView2 的 document-created 注入对 NavigateToString
        // （about:blank）文档不生效，故统一在 NavigationCompleted 后对活 document 补注入
        // （BridgeScript 带 document 级幂等守卫，跨导航重复执行不挂两遍监听器）。
        core.NavigationCompleted += OnNavigationCompleted;
        _subscribedCore = core;
        // 可观测：CoreWebView2 重建（sideload 重装 / re-init）后消息桥被重新订阅——暴露「订阅随旧实例丢失 → 回流断裂」的自愈动作。
        if (wasBound)
            _diagnostics.Report("HtmlChannel_message_bridge",
                "消息桥已随 CoreWebView2 重建重新订阅（WebMessageReceived/NavigationCompleted），消除旧实例订阅丢失。");
    }

    /// <summary>
    /// 等待点击桥在「当前 document」就绪（写回收敛）：快路径直查；否则挂起等待本次 NavigationCompleted 注入结果，超时兜底再查。
    /// </summary>
    public async Task<bool> ConvergeAsync(CancellationToken ct)
    {
        if (await QueryReadyAsync(ct).ConfigureAwait(false))
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navBridgeReadyTcs = tcs;
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMilliseconds(ConvergeTimeoutMs), ct)).ConfigureAwait(false);
        Interlocked.Exchange(ref _navBridgeReadyTcs, null);

        if (winner != tcs.Task || !tcs.Task.IsCompletedSuccessfully || !tcs.Task.Result)
        {
            // 兜底：导航可能在 TCS 注册前已快速完成，再查一次当前 document 桥就绪态。
            return await QueryReadyAsync(ct).ConfigureAwait(false);
        }
        return true;
    }

    private Task<bool> QueryReadyAsync(CancellationToken ct)
        => _marshaler.RunAsync(c => new ValueTask<bool>(WebViewBridgeProbe.IsBridgeReadyAsync(_host.Core)), ct);

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? json = null;
        try { json = e.TryGetWebMessageAsString(); }
        catch { json = null; }
        if (string.IsNullOrWhiteSpace(json))
        {
            // 点击桥（__domBridge）以「对象」形态 postMessage({cmd,...})：TryGetWebMessageAsString 对非字符串返回 null，
            // 若不兜底则对象形态消息被静默丢弃 → [data-cmd] 点击回流断裂；WebMessageAsJson 对对象/字符串均返回 JSON 表示。
            try { json = e.WebMessageAsJson; }
            catch { json = null; }
        }
        if (string.IsNullOrWhiteSpace(json))
            return;

        // WebView2 回调线程非 UI 线程：marshal 回 UI 线程再触碰控件状态/触发事件。
        var captured = json;
        _host.Post(() => _onMessageJson(captured));
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // P4：注入失败不再空 catch 吞掉——走统一重试确认 + 结构化诊断出口（文档未就绪等临时性失败自动重试）。
        try
        {
            var ok = await _bridgeHost.EnsureBridgeLoadedAsync(CancellationToken.None).ConfigureAwait(false);
            CompleteNavBridgeReady(ok);
            if (!ok)
                _diagnostics.Report("HtmlChannel_inject_bridge", "NavigationCompleted 后点击桥注入未就绪。");
        }
        catch (Exception ex)
        {
            CompleteNavBridgeReady(false);
            _diagnostics.Report("HtmlChannel_inject_bridge", $"NavigationCompleted 桥注入异常: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>写回收敛：完成等待中的 nav 桥就绪 TCS（导航后点击桥就绪结果）。</summary>
    private void CompleteNavBridgeReady(bool ok)
    {
        var tcs = Interlocked.Exchange(ref _navBridgeReadyTcs, null);
        tcs?.TrySetResult(ok);
    }
}
