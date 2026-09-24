#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Plate.HtmlViewer;
using TConfig;

namespace UiBackend.Html;

/// <summary>HTML 写回方式（宿主控件声明 <c>writeback</c> 驱动；缺省 Navigate 零回归）。</summary>
public enum HtmlWriteBackMode
{
    /// <summary>整页重载（<c>NavigateToString</c>）：整文档替换，滚动/焦点/展开态随之重置。</summary>
    Navigate,

    /// <summary>内容补丁（仅替换 body/容器 <c>innerHTML</c> 并保存恢复滚动、展开与焦点态）：不重载文档，UI 位置保持。</summary>
    Patch,
}

/// <summary>
/// HTML 后端运行时——把宿主注入的单一通道 <see cref="IHtmlChannel"/>（由 htmlviewer 实现）+ 声明控件树 +
/// 函数调用端口组装为事件桥/脚本桥的执行上下文。宿主在创建 htmlviewer 后装配。
/// 所有 WinForms→HTML 传递（整页渲染 / 页内 DOM 指令 / 事件回传）都经 <see cref="IHtmlChannel"/> 单一通道，CoreWebView2
/// 访问与 UI 线程封送收敛在通道实现（HtmlViewer）内。
/// </summary>
public sealed class HtmlBackendRuntime
{
    private readonly IHtmlChannel _channel;
    private readonly HtmlEventBridge.HtmlFunctionInvoker _invoker;
    private readonly HtmlEventBridge _eventBridge;
    private readonly HtmlScriptBridge _scriptBridge;
    private readonly IReadOnlyDictionary<string, ControlConfig> _controlsByName;
    // 宿主内容写回端口（DIP 剥离后的 Observe 收敛写抽象）：由连接层 FrontEndDriven 注入实现
    // （ObservedHtmlChannel 内部经 ObserveSessionManager + WebView2Transport）；null → 走纯渲染（无收敛语义）。
    private readonly HtmlWritePump? _writePump;

    // 已装配的宿主 htmlviewer 消息订阅（控件名 → 订阅；幂等，重复 AttachHostMessage 先解除旧订阅）
    private readonly Dictionary<string, IDisposable> _hostSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近一次写回内容（控件名 → 内容+是否成功）：内容未变则跳过写回，消除"变化驱动 + 1s 节流"下的空转 DOM 抖动。</summary>
    private readonly Dictionary<string, WriteRecord> _lastWrite = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已完成整页渲染（文档壳 + 主题 CSS 就位）的宿主控件：首次写回必须整页渲染（补丁只换 body，头/主题需先落地）。</summary>
    private readonly HashSet<string> _navigatedHosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct WriteRecord(string Html, bool Ok);

    /// <summary>写回方式声明值（ControlConfig.Writeback）→ 补丁模式。</summary>
    private const string PatchWritebackValue = "patch";

    public HtmlBackendRuntime(
        IHtmlChannel channel,
        IReadOnlyDictionary<string, ControlConfig> declaration,
        HtmlEventBridge.HtmlFunctionInvoker invoker,
        HtmlWritePump? writePump = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlsByName = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _writePump = writePump;
        Readiness = new HtmlRuntimeReadinessReport();
        _eventBridge = new HtmlEventBridge(declaration, channel, invoker);
        // 脚本桥与事件桥共享同一通道 + Invoker；单一实例，避免每次执行 new 新桥（消息桥单一化）。
        _scriptBridge = new HtmlScriptBridge(channel, invoker);
        // 聚合事件桥链路诊断到本运行时统一出口（宿主接线到日志门面）。
        _eventBridge.Diagnostics += Report;
    }

    public IHtmlChannel Channel => _channel;

    /// <summary>装配就绪报告：静态项由组合根填充，消息订阅项由 <see cref="AttachHostMessage"/> 更新。</summary>
    public HtmlRuntimeReadinessReport Readiness { get; }

    /// <summary>消息桥链统一诊断出口（category, message；宿主接线到日志门面，未接线为空操作）。
    /// 汇总通道（<see cref="IHtmlChannel"/>）webmessage 埋点 + 事件桥链路（route/invoke/writeback）埋点。</summary>
    public Action<string, string>? Diagnostics;

    private void Report(string category, string message) => Diagnostics?.Invoke(category, message);

    /// <summary>脚本桥命令协议 set/get/click/exec/sleep 落到 HTML 原生元素——运行时唯一实例。</summary>
    public HtmlScriptBridge ScriptBridge => _scriptBridge;

    /// <summary>注册函数调用端口（事件桥/脚本桥共用）。</summary>
    public HtmlEventBridge.HtmlFunctionInvoker Invoker => _invoker;

    public Task<string> HandleEventAsync(string payload, CancellationToken ct = default)
        => _eventBridge.HandleAsync(payload, ct);

    /// <summary>
    /// 按宿主控件声明 events["message"] 自动绑定 MessageReceived（声明驱动：函数名/参数映射/output 全部来自声明，不在硬编码）。
    /// 幂等：重复 attach 同一控件先解除旧订阅；声明未含 message 事件则不订阅（声明即开关）。
    /// 注册宿主内容写回端口（output 双通道：output=宿主控件名 → RenderHtmlAsync；contentSink 为写回副作用，如同步状态供命令 get 读回）。
    /// 不再静默 return——返回 null=绑定成功；返回非空字符串=失败原因（已记入 <see cref="Readiness"/> 并置 MessageSubscribed/HostWriterRegistered）。
    /// </summary>
    public string? AttachHostMessage(IHtmlChannel channel, string hostControlName, Action<string>? contentSink = null)
    {
        if (channel is null) throw new ArgumentNullException(nameof(channel));
        if (string.IsNullOrWhiteSpace(hostControlName))
        {
            Readiness.AddProblem("AttachHostMessage: hostControlName 为空，未绑定消息桥");
            return "hostControlName 为空，未绑定消息桥";
        }

        if (_hostSubscriptions.Remove(hostControlName, out var old))
            old.Dispose();

        // 域隔离解析：优先 host: 域键（生产域索引），回退裸名（兼容裸名索引/测试桩）。
        if (!_controlsByName.TryGetValue(hostControlName, out var control)
            && !_controlsByName.TryGetValue(HtmlDeclarationParser.HostKey(hostControlName), out control))
        {
            Readiness.AddProblem($"AttachHostMessage: 索引未命中控件 {hostControlName}（声明未含该控件，消息桥不会订阅）");
            return $"索引未命中控件 {hostControlName}";
        }
        if (!HasMessageEvent(control))
        {
            Readiness.AddProblem($"AttachHostMessage: 控件 {hostControlName} 未声明 events.message（声明即开关，消息桥未订阅）");
            return $"控件 {hostControlName} 未声明 events.message";
        }

        // output 双通道：宿主内容写回（output=宿主控件名 → 按声明写回方式：整页渲染 或 内容补丁）。
        // S1 统一写回：Navigate 经注入的 HtmlWritePump（连接层 Observe 收敛实现，保留 Converged/Degraded 语义）投递并等待「可观测」收敛，
        // 取代裸 RenderHtmlAsync fire-and-forget——消除「写回已应用但 DOM 未就绪 → 后续交互命中旧 DOM」窗口（通用状态同步框架 I1–I4）。
        // Patch（声明 writeback=patch）经 DOM 补丁替换内容：不重载文档，滚动/展开/焦点态保持（刷新不重置 UI 位置），
        // 补丁失败（桥未就绪/元素缺失）自动回退整页渲染，绝不静默丢刷新。
        // 未注入 pump（无 Observe 消费的宿主）→ 回退纯渲染（channel.RenderHtmlAsync），plate 仍不感知 Observe 类型。
        var writeMode = ResolveWriteBackMode(hostControlName);
        _eventBridge.RegisterHostWriter(hostControlName, async html =>
        {
            if (_lastWrite.TryGetValue(hostControlName, out var prev)
                && prev.Ok && string.Equals(prev.Html, html, StringComparison.Ordinal))
            {
                Report("EventBridge_writeback", $"写回 {hostControlName} 跳过（内容未变；mode={writeMode}）");
                return;
            }
            contentSink?.Invoke(html);
            if (writeMode == HtmlWriteBackMode.Patch && _navigatedHosts.Contains(hostControlName))
            {
                var status = HtmlElementNode.UnwrapString(
                    await channel.ExecuteAsync(DomOps.Patch, null, html).ConfigureAwait(false));
                if (string.Equals(status, "OK", StringComparison.Ordinal))
                {
                    _lastWrite[hostControlName] = new WriteRecord(html, true);
                    Report("EventBridge_writeback", $"补丁写回 {hostControlName}: OK（不重载文档，UI 位置保持）");
                    return;
                }
                Report("EventBridge_writeback",
                    $"补丁写回 {hostControlName} 未生效（{status}）→ 回退整页渲染");
            }
            if (_writePump is not null)
            {
                var result = await _writePump(html).ConfigureAwait(false);
                _lastWrite[hostControlName] = new WriteRecord(html, result.Status != HtmlWriteBackStatus.Failed);
                if (result.Status != HtmlWriteBackStatus.Failed)
                    _navigatedHosts.Add(hostControlName);
                Report("EventBridge_writeback",
                    $"收敛写回 {hostControlName}: result={result.Status}" +
                    (result.Detail is not null ? $", ack={result.Detail}" : "") +
                    (result.Window is not null ? $", window={result.Window.Value.TotalMilliseconds:F0}ms" : ""));
            }
            else
            {
                await channel.RenderHtmlAsync(html).ConfigureAwait(false);
                _lastWrite[hostControlName] = new WriteRecord(html, true);
                _navigatedHosts.Add(hostControlName);
                Report("EventBridge_writeback", $"写回 {hostControlName}（无收敛 pump，走纯渲染）");
            }
        });

        var handler = new EventHandler<HtmlMessageReceivedEventArgs>(async (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e?.Json))
                return;
            Report("HtmlChannel_webmessage", $"收到 postMessage: {TextUtil.Truncate(e.Json)}");
            try
            {
                // 单协议路由中枢：data-cmd="event:{name}" 已由点击桥翻译回事件负载（含 event+name）→ 页内声明事件 HandleAsync；
                // 否则（cmd 消息桥 selectTask/selectType 等）→ 宿主 events.message 路由。
                var (eventName, controlName, _) = HtmlEventBridge.ParsePayload(e.Json);
                if (!string.IsNullOrWhiteSpace(eventName) && !string.IsNullOrWhiteSpace(controlName))
                    await _eventBridge.HandleAsync(e.Json, CancellationToken.None).ConfigureAwait(false);
                else
                    await _eventBridge.HandleHostMessageAsync(hostControlName, e.Json, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 收敛静默 catch：不再空吞——记录错误分类 + 上下文（仍不向页面回抛，但必须有诊断出口）
                Report("HtmlChannel_webmessage_error", $"消息路由异常: {ex.GetType().Name}: {ex.Message}, 负载={TextUtil.Truncate(e.Json)}");
            }
        });
        channel.MessageReceived += handler;
        _hostSubscriptions[hostControlName] = new Subscription(channel, handler);

        Readiness.HostWriterRegistered = true;
        Readiness.MessageSubscribed = true;
        return null;
    }

    /// <summary>写回方式解析（声明驱动，单一真源）：宿主控件声明 <c>writeback="patch"</c> → 内容补丁；缺省 navigate（整页重载，零回归）。
    /// 域隔离同事件桥：先裸名、再 host: 域键。</summary>
    private HtmlWriteBackMode ResolveWriteBackMode(string hostControlName)
    {
        if (_controlsByName.TryGetValue(hostControlName, out var cfg)
            || _controlsByName.TryGetValue(HtmlDeclarationParser.HostKey(hostControlName), out cfg))
        {
            if (string.Equals(cfg.Writeback?.Trim(), PatchWritebackValue, StringComparison.OrdinalIgnoreCase))
                return HtmlWriteBackMode.Patch;
        }
        return HtmlWriteBackMode.Navigate;
    }

    private static bool HasMessageEvent(ControlConfig control)
    {
        if (control.Events?.EventsDictionary is not { Count: > 0 })
            return false;
        foreach (var key in control.Events.EventsDictionary.Keys)
            if (string.Equals(key, "message", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly IHtmlChannel _channel;
        private readonly EventHandler<HtmlMessageReceivedEventArgs> _handler;

        public Subscription(IHtmlChannel channel, EventHandler<HtmlMessageReceivedEventArgs> handler)
        {
            _channel = channel;
            _handler = handler;
        }

        public void Dispose() => _channel.MessageReceived -= _handler;
    }
}