using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UiBackend.Html;
using UiBackend.Abstractions;

namespace Plate.HtmlViewer;

/// <summary>
/// WinForms 内嵌 HTML 查看器（可信富文本通道）：持有 body HTML 片段 → <see cref="HtmlDocumentRenderer"/> 包页壳 →
/// WebView2.NavigateToString 显示。一次性 <see cref="WebView2.EnsureCoreWebView2Async"/>（需 Edge WebView2 运行时），
/// 之后可反复 <see cref="SetHtmlAsync"/> 更新内容 / <see cref="ApplyFontSizeAsync"/> 调整字号。
/// 注意：本控件是可信通道的唯一入口，自身不做任何解析/转义——XSS 防线在生产端渲染器（文本一律 HtmlEncode 后再入 body）。
/// </summary>
/// <remarks>
/// 交互支持：渲染端在 HTML 内输出 <c>[data-cmd]</c> 可交互元素（data-session/data-round 负载），点击桥注入后经
/// <see cref="CoreWebView2.WebMessageReceived"/> 收取并 <see cref="MessageReceived"/> 上报（已 marshal 回 UI 线程）。
/// 未订阅事件时仍是纯展示控件，行为向后兼容。
/// <para>本控件为**薄门面 + 组合根**：CoreWebView2 访问 / UI 线程封送 / 初始化生命周期 / 消息传输 / 回流探针 / 真实输入 /
/// 写回 / 订阅仲裁均下沉到 <c>UiBackend.Html</c> 的控件适配层组件（<see cref="IWebViewHost"/> ·
/// <see cref="WebViewLifecycleHost"/> · <see cref="WebViewMessageTransport"/> · <see cref="LoopbackProbe"/> ·
/// <see cref="RealInputClicker"/> · <see cref="ContentWriteCoordinator"/> · <see cref="MessageRouteArbiter"/>）；
/// 本类实现端口（<see cref="IWebViewHost"/> / <see cref="IWebViewContentProvider"/> / <see cref="IUiThreadAffine"/>）并转发。</para>
/// </remarks>
public sealed class HtmlViewer : WebView2, IHtmlChannel, IUiBridgeReadiness, IWebViewHost, IWebViewContentProvider
{
    // P4 注入自愈：渲染完成后同步确认点击桥注入；失败重试（毫秒级，换取真实点击确定性，见 WebViewLifecycleHost）。
    private const int BridgeRetryAttempts = 3;
    private const int BridgeRetryDelayMs = 50;

    private readonly UiThreadMarshaler _marshaler;
    private readonly HtmlScriptExecutor _scriptExecutor;
    private readonly HtmlScriptHost _bridgeHost;
    private readonly WebViewMessageTransport _messageTransport;
    private readonly WebViewLifecycleHost _lifecycle;
    private readonly LoopbackProbe _loopbackProbe;
    private readonly RealInputClicker _realInput;
    private readonly ContentWriteCoordinator _contentWriter;
    private readonly MessageRouteArbiter _messageRoutes;

    private bool _defaultShown;
    private string? _lastHtml;

    /// <summary>控件实例唯一标识（就绪报告据此断言「渲染实例 == 订阅实例」，防多实例错配）。</summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    public HtmlViewer()
    {
        // L0 控件适配层装配（唯一组合根）：UI 线程封送 / 参数化模板展开 / 桥注入生命周期 / 消息传输 / 回流探针 /
        // 真实输入 / 写回 / 订阅仲裁统一收敛到独立组件；本控件只保留「实现端口 + 转发 + 事件」。
        // UI 线程封送器在构造线程捕获 SynchronizationContext（供句柄未创建时的封送回退）。
        _marshaler = new UiThreadMarshaler(this);

        var assets = HtmlAssetRegistry.Default;
        _scriptExecutor = new HtmlScriptExecutor(this, assets);
        _bridgeHost = new HtmlScriptHost(this, _scriptExecutor, assets, BridgeRetryAttempts, BridgeRetryDelayMs);
        _bridgeHost.Diagnostics += ReportDiagnostics;
        _scriptExecutor.Diagnostics += ReportDiagnostics;

        _messageTransport = new WebViewMessageTransport(this, _bridgeHost, _marshaler, ProcessMessage, ReportDiagnostics);
        _lifecycle = new WebViewLifecycleHost(this, this, () => _messageTransport.EnsureInstalled(), ReportDiagnostics);
        _loopbackProbe = new LoopbackProbe(this, _marshaler, ct => _lifecycle.EnsureReadyAsync(ct), ReportDiagnostics);
        _realInput = new RealInputClicker(this, _scriptExecutor, ReportDiagnostics);
        _contentWriter = new ContentWriteCoordinator(ReportDiagnostics);
        _messageRoutes = new MessageRouteArbiter(h => MessageReceived += h, h => MessageReceived -= h);
    }

    /// <summary>HTML 文档基础字号（px）。默认 15（对齐 <see cref="HtmlDocumentRenderer.DefaultCss"/>）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double FontSizePx { get; set; } = 15;

    /// <summary>主题模式（深浅色显式锚定；默认 System 随系统 prefers-color-scheme）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    /// <summary>文档主题（CSS 片段）。缺省 null = 走全局 <see cref="HtmlTheme.Resolve"/>（宿主可注入 JSON 主题，否则内嵌默认）；控件级设置覆盖全局。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IHtmlTheme? Theme { get; set; }

    /// <summary>无内容时的默认占位 HTML（body 片段）；默认空态提示，避免页面加载期空白。设为 null/空白可关闭默认显示。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? DefaultHtml { get; set; } = "<p class=\"empty\">（空，等待内容）</p>";

    /// <summary>最后一次来自 HTML 的 postMessage 负载（解析为键值字典；非对象 JSON/解析失败为 null）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyDictionary<string, string>? LastMessagePayload { get; private set; }

    /// <summary>最后一次来自 HTML 的 postMessage 原始 JSON 字符串（尚无消息时为 null）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? LastMessagePayloadJson { get; private set; }

    /// <summary>HTML 侧点击可交互元素（<c>[data-cmd]</c>）postMessage 时触发；已在 UI 线程上（WebView2 回调已 marshal）。</summary>
    public event EventHandler<HtmlMessageReceivedEventArgs>? MessageReceived;

    /// <summary>桥链路结构化诊断出口（category, message 两元；宿主接线到日志门面，未接线时默认为空操作）。
    /// 注入失败 / 消息路由 / 写回等桥链各段经此上报，供阶段 2 映射为 fde_log_messages 的 msgId。</summary>
    public event Action<string, string>? BridgeDiagnostics;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // 首次可见即完成 WebView2 初始化并显示默认占位，避免页面加载期空白；内容写入后由 SetHtmlAsync 覆盖。
        if (Visible && !_defaultShown)
        {
            _defaultShown = true;
            _ = EnsureDefaultAsync();
        }
    }

    private async Task EnsureDefaultAsync()
    {
        try
        {
            await _lifecycle.EnsureReadyAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 初始化失败（缺 WebView2 运行时等）静默降级：保持空白，后续 SetHtmlAsync 会重试初始化。诊断经结构化出口上报。
            ReportDiagnostics("HtmlChannel_init_failed", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>渲染并显示 HTML（body 片段由 <see cref="HtmlDocumentRenderer"/> 包成完整页）。可任意次数调用；首次会完成 WebView2 初始化。
    /// WebView2 仅限 UI 线程访问，本方法在后台线程调用时经 UI 线程封送器封送回 UI 线程。
    /// <para><b>非生产写入口</b>：本方法是「整文档注入」原语（不重构语义，滚动/焦点/展开态全丢），只供控件内部（
    /// <see cref="WriteContentAsync"/> 无协调器时的回退）与首挂载使用。生产者一律经
    /// <see cref="WriteContentAsync"/> / <see cref="RenderPlanExecutor"/>（单写者），门禁 <c>Check-UiSingleWrite</c> 强制。</para></summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Task SetHtmlAsync(string? html, CancellationToken ct = default)
        => _marshaler.RunAsync(c => new ValueTask(_lifecycle.RenderAsync(html, c)), ct);

    /// <summary>宿主内容写回协调器（D2 单写者）：由 <see cref="UiBackend.Html.HtmlBackendRuntime"/> 在装配时挂接。
    /// 挂接后，所有「输出 → 页面内容」的落地（值契约 / ECS 输出 / 消息桥）统一经协调器（reconcile / 去重 / 区域，保态）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string, UiBackend.Abstractions.UiWriteSource, CancellationToken, Task>? HostContentWriter
    {
        get => _contentWriter.Writer;
        set => _contentWriter.Writer = value;
    }

    /// <summary>写回计划执行器（单写落地 B3）：由 <see cref="UiBackend.Html.HtmlBackendRuntime"/> 装配时挂接；
    /// 携带 <see cref="UiBackend.Abstractions.RenderOutput.Plan"/> 的产物经此落地（替代对 ToString 的静默丢弃）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<UiBackend.Abstractions.UiWritePlan, Task>? RenderPlanExecutor
    {
        get => _contentWriter.PlanExecutor;
        set => _contentWriter.PlanExecutor = value;
    }

    /// <summary>抢占式获取消息订阅权（撤销同控件名旧 owner）——后到者接管，顺序无关。</summary>
    public IDisposable AcquireMessageChannel(string controlName, EventHandler<HtmlMessageReceivedEventArgs> handler)
        => _messageRoutes.Acquire(controlName, handler);

    /// <summary>仅当控件名未被占用时获取订阅权（返回 null = 已由他方持有）。</summary>
    public IDisposable? TryAcquireMessageChannelIfIdle(string controlName, EventHandler<HtmlMessageReceivedEventArgs> handler)
        => _messageRoutes.TryAcquireIfIdle(controlName, handler);

    /// <summary>控件名是否已有消息订阅 owner。</summary>
    public bool HasMessageChannelOwner(string controlName)
        => _messageRoutes.HasOwner(controlName);


    /// <summary>内容写回入口（D2）：有协调器 ⇒ 经协调器落页（不重载文档，保展开 / 滚动 / 焦点）；
    /// 无协调器 ⇒ 回退 <see cref="SetHtmlAsync"/> 整页渲染（首挂载或未装配协调器的宿主）。
    /// <paramref name="source"/> 为写回来源（缺省 Mount；交互/刷新/流必须显式标注，否则优先级失效）。</summary>
    public Task WriteContentAsync(string? html, CancellationToken ct = default,
        UiBackend.Abstractions.UiWriteSource source = UiBackend.Abstractions.UiWriteSource.Mount)
        => _contentWriter.WriteAsync(html, source, ct, (h, c) => SetHtmlAsync(h, c));

    /// <summary>显式清空（整文档重置）：有计划执行器 ⇒ 经协调器整页写（单写者，不产生第二写路径）；
    /// 无协调器 ⇒ 控件内部整页回退。清空不可用「空内容写」表达（空写按无写回短路）。</summary>
    public Task ClearContentAsync()
        => RenderPlanExecutor is { } executor
            ? executor(UiBackend.Abstractions.UiWritePlan.FullDocument(string.Empty))
            : SetHtmlAsync(string.Empty);

    /// <summary>值契约 / 同步出口触发的写回（fire-and-forget）：写回失败经 <see cref="BridgeDiagnostics"/> 上报，
    /// 不以「未观察 Task 异常」逃逸（F2）；需要 await 语义的调用方仍用 <see cref="WriteContentAsync"/>。</summary>
    public void WriteContentDetached(string? html,
        UiBackend.Abstractions.UiWriteSource source = UiBackend.Abstractions.UiWriteSource.Mount)
        => _contentWriter.WriteDetached(html, source, (h, c) => SetHtmlAsync(h, c));

    /// <summary>当前生效主题文档：控件级 <see cref="Theme"/>（IHtmlTheme 兼容）优先，未设置经全局 <see cref="HtmlTheme.ResolveProvider"/> 解析（主题单一源，唯一收敛点）。</summary>
    private ThemeDocument ResolveThemeDocument()
        => Theme is not null ? ThemeDocument.FromCss(Theme.Css) : HtmlTheme.ResolveProvider().Resolve(ThemeMode, ThemeScope.Document);

    /// <summary>仅更新字号并重渲染当前内容（若有）；无内容时仅记录字号，下次渲染生效。UI 线程封送同 <see cref="SetHtmlAsync"/>。</summary>
    public Task ApplyFontSizeAsync(double fontSizePx, CancellationToken ct = default)
        => _marshaler.RunAsync(c => new ValueTask(_lifecycle.ApplyFontSizeAsync(fontSizePx, c)), ct);

    /// <summary>宿主 → 页面 DOM 指令通道：经 __domBridge 对 data-name 元素执行受控操作（op 白名单 set/get/click/bounds/event/realclick）。
    /// 返回页面执行结果原始 JSON 字符串（get 为字符串字面量，其余 'OK'/'NOT_FOUND'）。后台线程调用时封送 UI 线程（WebView2 仅限 UI 线程）。</summary>
    public Task<string> ExecuteDomAsync(string op, string name, string? value, CancellationToken ct = default)
        => _marshaler.RunAsync(c => new ValueTask<string>(ExecuteDomCoreAsync(op, name, value, c)), ct);

    private async Task<string> ExecuteDomCoreAsync(string op, string name, string? value, CancellationToken ct)
    {
        await _lifecycle.EnsureReadyAsync(ct);
        if (CoreWebView2 is null) return string.Empty;
        // realclick 走真实输入层（Win32 SendInput，isTrusted=true），不走 JS __domBridge 合成事件。
        if (op == DomOps.RealClick)
            return await _realInput.ClickAsync(name, ct);
        // 幂等补注入桥脚本：DOM 指令强依赖 __domBridge，可能早于 NavigationCompleted 触发（渲染后立即 set/get）。
        // 注入失败（document 未就绪/导航中/脚本执行异常）时返回明确诊断，不再对不存在的 __domBridge 强调 handle——
        // 否则 ExecuteScriptAsync 返回误导性 null，掩盖「点击桥注入失败」根因（见日志 click → null 假象）。
        if (!await _bridgeHost.EnsureBridgeLoadedAsync(ct))
            return "BRIDGE_NOT_READY";
        // 自愈（防部分注入）：patch/reconcile 依赖键控对齐模块 window.__uiReconcile 与桥模块清单；
        // 若注入在点击桥之后中断，区域写会返回 STATE_NOT_READY → 调用方整页回退（折叠态重置）。
        // 此处按模块清单探测缺失即补注入全部文档脚本（幂等守卫防重复），使写原语可自愈而非静默降级。
        if ((op == DomOps.Patch || op == DomOps.Reconcile || op == DomOps.Append || op == DomOps.Trim) && CoreWebView2 is not null)
        {
            var modules = BridgeModuleManifest.Parse(
                await CoreWebView2.ExecuteScriptAsync(BridgeModuleManifest.QueryScript));
            if (!modules.IsComplete)
            {
                ReportDiagnostics("HtmlChannel_inject_bridge",
                    $"桥模块未全就绪（缺 {modules.Missing}）→ 补注入全部文档脚本自愈（防区域写 STATE_NOT_READY 降级）。");
                await _bridgeHost.InjectAllAsync(ct);
            }
        }
        // L2 执行层统一渲染 __domBridge.handle 占位模板（参数 JSON 序列化，防引号转义漂移）并执行。
        return await _scriptExecutor.ExecuteHandleAsync(op, name, value, ct);
    }

    /// <summary>回流链路自检（<see cref="IHtmlChannel.LoopbackProbeAsync"/>）：注入 __loopback postMessage 并等消息桥收到同一 nonce。
    /// 整个探针（含初始化）封送 UI 线程，兼容脚本/工作线程调用。</summary>
    public Task<bool> LoopbackProbeAsync(CancellationToken ct = default)
        => _loopbackProbe.ProbeAsync(ct);

    private void ProcessMessage(string json)
    {
        LastMessagePayloadJson = json;
        ReportDiagnostics("HtmlChannel_webmessage", $"收到 postMessage: {TextUtil.Truncate(json)}");
        try
        {
            LastMessagePayload = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            LastMessagePayload = null;
        }
        // P0 回流探针消费：__loopback__ 自检负载仅用于确认「postMessage → 本实例 C# 侧」闭环，完成后不冒泡到消息桥/外部事件。
        if (_loopbackProbe.TryConsume(LastMessagePayload))
            return;
        MessageReceived?.Invoke(this, new HtmlMessageReceivedEventArgs(json, LastMessagePayload));
    }

    /// <summary>桥链路结构化诊断上报（经 <see cref="BridgeDiagnostics"/> 出口；未接线时兜底到 Trace，保证关键断点不因 sink 缺失而静默）。</summary>
    private void ReportDiagnostics(string category, string message)
    {
        BridgeDiagnostics?.Invoke(category, message);
        System.Diagnostics.Trace.WriteLine($"[{category}] {message}");
    }


    // ── IUiThreadAffine / IWebViewHost / IWebViewContentProvider 端口实现（由控件适配层组件消费）──

    Task IUiThreadAffine.InvokeOnUiAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
        => InvokeAsync(action, ct);

    Task<T> IUiThreadAffine.InvokeOnUiAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
        => InvokeAsync(action, ct);

    // 句柄创建通知（显式接口实现）：封送器在「句柄未创建 + 跨线程」时据此延后到句柄线程执行。
    event EventHandler? IUiThreadAffine.HandleCreated
    {
        add => HandleCreated += value;
        remove => HandleCreated -= value;
    }

    CoreWebView2? IWebViewCore.Core => CoreWebView2;

    Task IWebViewCore.EnsureCoreAsync() => EnsureCoreWebView2Async();

    void IWebViewCore.NavigateToHtmlDocument(string html) => CoreWebView2?.NavigateToString(html);

    System.Drawing.Color IWebViewCore.DefaultBackgroundColor
    {
        get => DefaultBackgroundColor;
        set => DefaultBackgroundColor = value;
    }

    void IWebViewHost.Post(Action action)
    {
        // WebView2 回调线程非 UI 线程：句柄已创建则 BeginInvoke 封送（与原行为一致）；否则同步执行（headless/挂载前）。
        if (IsDisposed || !IsHandleCreated)
        {
            action();
            return;
        }
        try { BeginInvoke(action); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    void IWebViewHost.ActivateForInput()
    {
        var form = FindForm();
        if (form is null) return;
        try
        {
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;
            if (!form.Focused)
                form.Activate();
            form.BringToFront();
            if (!Focused)
                Focus(); // 让 WebView2 获得输入焦点（Chromium 聚焦）
        }
        catch { }
    }

    IntPtr IWebViewHost.HostFormHandle => FindForm()?.Handle ?? IntPtr.Zero;

    ThemeDocument IWebViewContentProvider.ResolveTheme() => ResolveThemeDocument();

    ThemeMode IWebViewContentProvider.Mode => ThemeMode;

    double IWebViewContentProvider.FontSizePx
    {
        get => FontSizePx;
        set => FontSizePx = value;
    }

    string? IWebViewContentProvider.DefaultBodyHtml => DefaultHtml;

    string? IWebViewContentProvider.LastBodyHtml
    {
        get => _lastHtml;
        set => _lastHtml = value;
    }

    string IWebViewContentProvider.RenderDocument(string? bodyHtml)
        => HtmlDocumentRenderer.ToHtml(bodyHtml, ResolveThemeDocument(), ThemeMode, FontSizePx);


    // IHtmlChannel 显式实现：整页渲染 + 页内 DOM 指令统一入口（CoreWebView2 访问收敛到本控件唯一封送点）。
    // P4 写回收敛：消息桥重渲染写回（hostWriter → RenderHtmlAsync）导航后等待点击桥就绪，确保重渲染产物（含 data-cmd/data-name
    // 交互元素）落地到存活 document 才返回——消除「写回 fire-and-forget → 后续脚本点击命中旧 DOM → 任务/视图点击无效/NOT_FOUND」缺陷。
    async Task IHtmlChannel.RenderHtmlAsync(string html, CancellationToken ct)
    {
        await SetHtmlAsync(html, ct).ConfigureAwait(false);
        if (!await _messageTransport.ConvergeAsync(ct).ConfigureAwait(false))
            ReportDiagnostics("HtmlChannel_writeback_not_converged", "RenderHtmlAsync 写回后点击桥未就绪（重渲染产物可能未落地）。");
    }

    // 收敛写已上移连接层 FrontEndDriven（ObservedHtmlChannel 装饰器 = ObserveSessionManager + WebView2Transport 适配本通道），
    // 本控件不再内建 Observe 依赖（DIP 剥离）；如需收敛写，由组合根包一层 IObservedHtmlChannel 后使用。
    Task<string> IHtmlChannel.ExecuteAsync(string op, string name, string? value, CancellationToken ct)
        => ExecuteDomAsync(op, name, value, ct);

    Task<bool> IHtmlChannel.IsBridgeReadyAsync(CancellationToken ct)
        => _marshaler.RunAsync(c => new ValueTask<bool>(WebViewBridgeProbe.IsBridgeReadyAsync(CoreWebView2)), ct);

    Task<UiBridgeModuleState> IUiBridgeReadiness.GetBridgeModulesAsync(CancellationToken ct)
        => _marshaler.RunAsync(c => new ValueTask<UiBridgeModuleState>(WebViewBridgeProbe.GetBridgeModulesAsync(CoreWebView2)), ct);

    /// <summary>宿主 → 页面反向推送（事件驱动刷新）：PostWebMessageAsJson 投递到活 document，页面脚本经 message 事件接收。
    /// 未就绪 / 无 CoreWebView2 / 投递异常 → false + 结构化诊断（不冒充成功）。</summary>
    Task<bool> IHtmlChannel.PostToPageAsync(string json, CancellationToken ct)
        => _marshaler.RunAsync(c => new ValueTask<bool>(PostToPageCoreAsync(json, c)), ct);

    private async Task<bool> PostToPageCoreAsync(string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        await _lifecycle.EnsureReadyAsync(ct);
        if (CoreWebView2 is null)
        {
            ReportDiagnostics("HtmlChannel_post_to_page", "推送未送达: CoreWebView2 未就绪（初始化失败/未加载）");
            return false;
        }
        try
        {
            CoreWebView2.PostWebMessageAsJson(json);
            return true;
        }
        catch (Exception ex)
        {
            ReportDiagnostics("HtmlChannel_post_to_page", $"推送未送达: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>原生脚本执行通道（让 L2 执行层 / L3 生命周期层的模板展开与桥注入复用本控件唯一 WebView2 封送点）。
    /// 就绪前 / 无 CoreWebView2 时返回空串；返回页面执行结果原文。后台线程调用自动封送 UI 线程。</summary>
    private async Task<string> ExecuteRootScriptCoreAsync(string javaScript, CancellationToken ct)
    {
        await _lifecycle.EnsureReadyAsync(ct);
        if (CoreWebView2 is null) return string.Empty;
        return await CoreWebView2.ExecuteScriptAsync(javaScript);
    }

    Task<string> IHtmlChannel.ExecuteScriptAsync(string javaScript, CancellationToken ct)
        => _marshaler.RunAsync(c => new ValueTask<string>(ExecuteRootScriptCoreAsync(javaScript, c)), ct);
}

