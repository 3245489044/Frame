using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UiBackend.Html;

namespace Plate.HtmlViewer;

/// <summary>
/// WinForms 内嵌 HTML 查看器（可信富文本通道）：持有 body HTML 片段 → <see cref="HtmlDocumentRenderer"/> 包页壳 →
/// WebView2.NavigateToString 显示。一次性 <see cref="WebView2.EnsureCoreWebView2Async"/>（需 Edge WebView2 运行时），
/// 之后可反复 <see cref="SetHtmlAsync"/> 更新内容 / <see cref="ApplyFontSizeAsync"/> 调整字号。
/// 注意：本控件是可信通道的唯一入口，自身不做任何解析/转义——XSS 防线在生产端渲染器（文本一律 HtmlEncode 后再入 body）。
/// </summary>
/// <remarks>
/// 交互支持：渲染端在 HTML 内输出 <c>[data-cmd]</c> 可交互元素（data-session/data-round 负载），本控件注入点击桥
/// （<c>data-cmd=selectHistory</c> → 客户端滚动，其余 → <c>window.chrome.webview.postMessage</c>），
/// 经 <see cref="CoreWebView2.WebMessageReceived"/> 收取后 <see cref="MessageReceived"/> 上报（已 marshal 回 UI 线程）。
/// 未订阅事件时仍是纯展示控件，行为向后兼容。
/// </remarks>
public sealed class HtmlViewer : WebView2, IHtmlChannel
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _ready;
    private bool _defaultShown;
    private string? _lastHtml;
    // 消息桥订阅目标 CoreWebView2（P0/P3）：记录已订阅 WebMessageReceived 的 CoreWebView2 实例引用；
    // 若控件 CoreWebView2 被重建（sideload 重装 / re-init），下次 EnsureReadyAsync 检测到引用变化即重新绑定，
    // 消除「DOM 已注入桥、postMessage 已发出，但 C# WebMessageReceived 订阅随旧实例丢失 → 点击回流断裂」缺陷。
    private CoreWebView2? _subscribedCore;

    // UI 线程封送回退（句柄未创建时）：控件在 UI 线程创建（EcsGate/设计器），构造时捕获 WindowsFormsSynchronizationContext。
    // 挂载前 IsHandleCreated=false 无法用 InvokeAsync 封送，但后台线程（FunctionExecutor → HtmlAttachAsync）可能先行触发
    // EnsureCoreWebView2Async——不经此回退会在后台线程直接执行而抛「EnsureCoreWebView2Async 仅限 UI 线程」，且 CoreWebView2 恒 null
    // → WebMessageReceived 永不订阅 → DOM postMessage 到不了 C# → 回流断裂（8.26 实证）。null 时保持旧行为（直接执行）。
    private readonly SynchronizationContext? _uiSyncContext;

    // P4 写回收敛：记录「最近一次写回导航后等待点击桥就绪」的协调器，供 ConvergeBridgeAsync 等待 NavigationCompleted 注入完成，
    // 消除「RenderHtmlAsync fire-and-forget → 重渲染产物未落地 → data-cmd/data-name 交互元素缺失 → 点击无效」链路缺陷（8.26 阶段0）。
    private TaskCompletionSource<bool>? _navBridgeReadyTcs;

    /// <summary>控件实例唯一标识（就绪报告据此断言「渲染实例 == 订阅实例」，防多实例错配）。</summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    // P0 回流探针：pending 的 __loopback__ 自检等待者（nonce → TCS），由 ProcessMessage 收到对应负载时完成。
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _loopbackWaiters = new();
    private long _loopbackSeq;

    // P0 回流探针自愈重试：装配时序/导航竞态导致的瞬时断裂（首次初始化未稳 / 消息桥订阅随 CoreWebView2 重建丢失）自动重试恢复。
    private const int LoopbackProbeAttempts = 3;
    private const int LoopbackProbeDelayMs = 150;

    // P4 注入自愈：渲染完成后同步确认点击桥注入；失败重试（毫秒级，换取真实点击确定性，见 SetHtmlCoreAsync）。
    private const int BridgeRetryAttempts = 3;
    private const int BridgeRetryDelayMs = 50;

    // 执行层 + 生命周期层：参数化模板展开 / 桥注入生命周期收敛（CoreWebView2 访问仍收敛在本控件唯一封送点）。
    private readonly HtmlScriptExecutor _scriptExecutor;
    private readonly HtmlScriptHost _bridgeHost;

    public HtmlViewer()
    {
        // 捕获构造线程的 SynchronizationContext（UI 线程创建控件时为 WindowsFormsSynchronizationContext），
        // 供 RunOnUiThreadAsync 在控件句柄未创建时封送回 UI 线程（WebView2 的 EnsureCoreWebView2Async 仅限 UI 线程）。
        _uiSyncContext = SynchronizationContext.Current;

        // L1 资产层（具名资产）→ L2 执行层 → L3 生命周期层：桥脚本 / DOM 指令模板 / 注入自愈统一收敛，
        // 本控件只保留「CoreWebView2 访问 + UI 线程封送 + 事件」的通道职责。
        var assets = HtmlAssetRegistry.Default;
        _scriptExecutor = new HtmlScriptExecutor(this, assets);
        _bridgeHost = new HtmlScriptHost(this, _scriptExecutor, assets, BridgeRetryAttempts, BridgeRetryDelayMs);
        _bridgeHost.Diagnostics += ReportDiagnostics;
        _scriptExecutor.Diagnostics += ReportDiagnostics;
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
            await EnsureReadyAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 初始化失败（缺 WebView2 运行时等）静默降级：保持空白，后续 SetHtmlAsync 会重试初始化。诊断经结构化出口上报。
            ReportDiagnostics("HtmlChannel_init_failed", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>渲染并显示 HTML（body 片段由 <see cref="HtmlDocumentRenderer"/> 包成完整页）。可任意次数调用；首次会完成 WebView2 初始化。
    /// WebView2 仅限 UI 线程访问，本方法在后台线程调用时经 <see cref="Control.InvokeAsync"/> 封送回 UI 线程。</summary>
    public Task SetHtmlAsync(string? html, CancellationToken ct = default)
        => RunOnUiThreadAsync(c => new ValueTask(SetHtmlCoreAsync(html, c)), ct);

    /// <summary>当前生效主题文档：控件级 <see cref="Theme"/>（IHtmlTheme 兼容）优先，未设置经全局 <see cref="HtmlTheme.ResolveProvider"/> 解析（主题单一源，唯一收敛点）。</summary>
    private ThemeDocument ResolveThemeDocument()
        => Theme is not null ? ThemeDocument.FromCss(Theme.Css) : HtmlTheme.ResolveProvider().Resolve(ThemeMode, ThemeScope.Document);

    private async Task SetHtmlCoreAsync(string? html, CancellationToken ct)
    {
        _lastHtml = html;
        await EnsureReadyAsync(ct);
        if (CoreWebView2 is not null)
            CoreWebView2.NavigateToString(HtmlDocumentRenderer.ToHtml(html, ResolveThemeDocument(), ThemeMode, FontSizePx));
        // 桥注入就绪确认统一由 NavigationCompleted 订阅 → L3 生命周期层 HtmlScriptHost.EnsureBridgeLoadedAsync 完成，
        // 此处不再同步复验，避免每次渲染双重验证与重复诊断。DOM 指令路径在 ExecuteDomCoreAsync 内自行 EnsureBridgeLoaded。
    }

    /// <summary>仅更新字号并重渲染当前内容（若有）；无内容时仅记录字号，下次渲染生效。UI 线程封送同 <see cref="SetHtmlAsync"/>。</summary>
    public Task ApplyFontSizeAsync(double fontSizePx, CancellationToken ct = default)
        => RunOnUiThreadAsync(c => new ValueTask(ApplyFontSizeCoreAsync(fontSizePx, c)), ct);

    private async Task ApplyFontSizeCoreAsync(double fontSizePx, CancellationToken ct)
    {
        if (fontSizePx > 0)
            FontSizePx = fontSizePx;
        if (_lastHtml is null)
            return;
        await EnsureReadyAsync(ct);
        if (CoreWebView2 is not null)
            CoreWebView2.NavigateToString(HtmlDocumentRenderer.ToHtml(_lastHtml, ResolveThemeDocument(), ThemeMode, FontSizePx));
    }

    /// <summary>若控件在非 UI 线程访问（InvokeRequired）则封送到 UI 线程执行（WebView2 仅限 UI 线程）；否则直接执行。
    /// 三路封送：① 句柄已创建 → <see cref="Control.InvokeAsync"/>；② 后台线程但句柄未创建（sideload/EcsGate 挂载前）→ 回退构造时捕获的
    /// SynchronizationContext.Post（对齐 UiDispatcherCore「句柄未创建回退 SynchronizationContext」语义）；③ 已在 UI 线程 → 直接执行。
    /// WFO2001：委托须接收 CancellationToken 且返回 ValueTask（Func&lt;CancellationToken,ValueTask&gt; 重载，单次 await）。</summary>
    private async Task RunOnUiThreadAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        if (InvokeRequired && IsHandleCreated)
            await InvokeAsync(action, ct);
        else if (InvokeRequired && _uiSyncContext is not null)
            await InvokeOnUiSyncContextAsync(action, ct);
        else
            await action(ct);
    }

    /// <summary>带返回值的 UI 线程封送（Func&lt;CancellationToken,ValueTask&lt;T&gt;&gt; 重载，单次 await）。三路封送语义同无返回值版。</summary>
    private async Task<T> RunOnUiThreadAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        if (InvokeRequired && IsHandleCreated)
            return await InvokeAsync(action, ct);
        if (InvokeRequired && _uiSyncContext is not null)
            return await InvokeOnUiSyncContextAsync(action, ct);
        return await action(ct);
    }

    /// <summary>经捕获的 SynchronizationContext 在 UI 线程执行异步动作（句柄未创建时的封送回退）。带 5s 超时兜底，防 UI 泵阻塞时无限挂起。</summary>
    private Task InvokeOnUiSyncContextAsync(Func<CancellationToken, ValueTask> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = _uiSyncContext!;
        ctx.Post(async _ =>
        {
            try
            {
                await action(ct).ConfigureAwait(false);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return RaceWithUiSyncTimeoutAsync(tcs.Task);
    }

    /// <summary>经捕获的 SynchronizationContext 在 UI 线程执行异步动作并返回结果（句柄未创建时的封送回退）。带 5s 超时兜底。</summary>
    private Task<T> InvokeOnUiSyncContextAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = _uiSyncContext!;
        ctx.Post(async _ =>
        {
            try
            {
                var r = await action(ct).ConfigureAwait(false);
                tcs.TrySetResult(r);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return RaceWithUiSyncTimeoutAsync(tcs.Task);
    }

    /// <summary>SynchronizationContext.Post 封送等待超时（毫秒，对齐 UiDispatcherCore 的 5s 语义）。</summary>
    private const int UiSyncDispatchTimeoutMs = 5000;

    private async Task RaceWithUiSyncTimeoutAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(UiSyncDispatchTimeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException("UI 线程封送回退（SynchronizationContext.Post）等待超时（5s）：UI 泵可能被同步阻塞。");
        await task.ConfigureAwait(false);
    }

    private async Task<T> RaceWithUiSyncTimeoutAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(UiSyncDispatchTimeoutMs)).ConfigureAwait(false);
        if (completed != task)
            throw new TimeoutException("UI 线程封送回退（SynchronizationContext.Post）等待超时（5s）：UI 泵可能被同步阻塞。");
        return await task.ConfigureAwait(false);
    }

    private async Task EnsureReadyAsync(CancellationToken ct)
    {
        await _initLock.WaitAsync(ct);
        try
        {
            if (!_ready)
            {
                await EnsureCoreWebView2Async();
                // 深浅色显式锚定：按 ThemeMode 锚定 WebView2 配色与默认背景，避免「随系统猜测而黑」/ 周边底色不一致。
                if (CoreWebView2?.Profile is not null)
                    CoreWebView2.Profile.PreferredColorScheme = ThemeMode.Map();
                var bg = HtmlThemeBackground.Resolve(ResolveThemeDocument().ToCss(), ThemeMode);
                if (bg.HasValue)
                    DefaultBackgroundColor = bg.Value;
                _ready = true;
                // 无内容时显示默认占位（仅在从未 SetHtmlAsync 时；SetHtmlAsync 会先设 _lastHtml 再进来）。
                if (CoreWebView2 is not null && _lastHtml is null && !string.IsNullOrWhiteSpace(DefaultHtml))
                    CoreWebView2.NavigateToString(HtmlDocumentRenderer.ToHtml(DefaultHtml, ResolveThemeDocument(), ThemeMode, FontSizePx));
            }
            // 消息桥订阅：首次就绪 + CoreWebView2 重建后重新绑定（幂等：同实例引用则跳过，避免重复订阅）。
            EnsureMessageBridgeInstalled();
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 绑定 WebView2 消息桥：订阅 <see cref="CoreWebView2.WebMessageReceived"/> + 导航后点击桥注入。
    /// 幂等：仅当 CoreWebView2 实例引用变化（首次就绪 / 控件重建）时才（重新）绑定，同一实例重复调用跳过——防重复订阅。
    /// 点击桥把 <c>[data-cmd]</c> 元素负载 postMessage 到宿主（P5 单协议：cmd=event:{eventName} 由桥翻译回事件负载
    /// 供声明式事件桥；selectTask/selectType 等直通消息桥）；<c>selectHistory</c> 仅客户端滚动不打扰宿主。
    /// </summary>
    private void EnsureMessageBridgeInstalled()
    {
        if (CoreWebView2 is null || ReferenceEquals(_subscribedCore, CoreWebView2))
            return;
        var wasBound = _subscribedCore is not null;

        CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // 桥脚本仅在"活 document"上注入：WebView2 的 AddScriptToExecuteOnDocumentCreatedAsync 对 NavigateToString
        // （about:blank）文档不生效，故无需注册 document-created 注入；统一在 NavigationCompleted 后对活 document
        // 补注入（BridgeScript 带 __domBridgeInstalled 幂等守卫，跨导航重复执行不挂两遍监听器）。
        CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _subscribedCore = CoreWebView2;
        // 可观测：CoreWebView2 重建（sideload 重装 / re-init）后消息桥被重新订阅——暴露「订阅随旧实例丢失 → 回流断裂」的自愈动作，
        // 使「点击无效却无日志」的断链在日志可见（对接 HtmlChannel_message_bridge msgId）。
        if (wasBound)
            ReportDiagnostics("HtmlChannel_message_bridge",
                "消息桥已随 CoreWebView2 重建重新订阅（WebMessageReceived/NavigationCompleted），消除旧实例订阅丢失。");
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // P4：注入失败不再空 catch 吞掉——走统一重试确认 + 结构化诊断出口（文档未就绪等临时性失败自动重试）。
        try
        {
            var ok = await _bridgeHost.EnsureBridgeLoadedAsync(CancellationToken.None);
            CompleteNavBridgeReady(ok);
            if (!ok)
                ReportDiagnostics("HtmlChannel_inject_bridge", "NavigationCompleted 后点击桥注入未就绪。");
        }
        catch (Exception ex)
        {
            CompleteNavBridgeReady(false);
            ReportDiagnostics("HtmlChannel_inject_bridge", $"NavigationCompleted 桥注入异常: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>写回收敛：完成等待中的 <see cref="_navBridgeReadyTcs"/>（导航后点击桥就绪结果）。</summary>
    private void CompleteNavBridgeReady(bool ok)
    {
        var tcs = System.Threading.Interlocked.Exchange(ref _navBridgeReadyTcs, null);
        tcs?.TrySetResult(ok);
    }

    /// <summary>
    /// 等待点击桥在「当前 document」就绪（写回收敛）：导航后重渲染产物只有等桥注入完成才算真正落地，
    /// 否则后续 DOM 指令/脚本点击会命中旧 document（桥已就绪但交互元素缺失）→ NOT_FOUND/点击无效。
    /// 快路径：桥已就绪直接返回；否则挂起等待本次 NavigationCompleted 注入结果，超时兜底再查一次。
    /// </summary>
    private async Task<bool> ConvergeBridgeAsync(CancellationToken ct)
    {
        if (await RunOnUiThreadAsync(c => new ValueTask<bool>(IsBridgeReadyCoreAsync(c)), ct).ConfigureAwait(false))
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navBridgeReadyTcs = tcs;
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMilliseconds(2500), ct)).ConfigureAwait(false);
        System.Threading.Interlocked.Exchange(ref _navBridgeReadyTcs, null);

        if (winner != tcs.Task || !tcs.Task.IsCompletedSuccessfully || !tcs.Task.Result)
        {
            // 兜底：导航可能在 TCS 注册前已快速完成，再查一次当前 document 桥就绪态。
            return await RunOnUiThreadAsync(c => new ValueTask<bool>(IsBridgeReadyCoreAsync(c)), ct).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>
    /// 宿主 → 页面 DOM 指令通道：经 __domBridge 对 data-name 元素执行受控操作（op 白名单 set/get/click/bounds/event）。
    /// 返回页面执行结果原始 JSON 字符串（get 为字符串字面量，其余 'OK'/'NOT_FOUND'）。后台线程调用时封送 UI 线程（WebView2 仅限 UI 线程）。
    /// </summary>
    public Task<string> ExecuteDomAsync(string op, string name, string? value, CancellationToken ct = default)
        => RunOnUiThreadAsync(c => new ValueTask<string>(ExecuteDomCoreAsync(op, name, value, c)), ct);

    private async Task<string> ExecuteDomCoreAsync(string op, string name, string? value, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);
        if (CoreWebView2 is null) return string.Empty;
        // realclick 走真实输入层（Win32 SendInput，isTrusted=true），不走 JS __domBridge 合成事件
        if (op == DomOps.RealClick)
            return await RealClickCoreAsync(name, ct);
        // 幂等补注入桥脚本：DOM 指令强依赖 __domBridge，可能早于 NavigationCompleted 触发（渲染后立即 set/get）。
        // 注入失败（document 未就绪/导航中/脚本执行异常）时返回明确诊断，不再对不存在的 __domBridge 强调 handle——
        // 否则 ExecuteScriptAsync 返回误导性 null，掩盖「点击桥注入失败」根因（见日志 click → null 假象）。
        if (!await _bridgeHost.EnsureBridgeLoadedAsync(ct))
            return "BRIDGE_NOT_READY";
        // L2 执行层统一渲染 __domBridge.handle 占位模板（参数 JSON 序列化，防引号转义漂移）并执行。
        return await _scriptExecutor.ExecuteHandleAsync(op, name, value, ct);
    }

    /// <summary>桥注入 / DOM 指令模板展开已收敛到 L2 执行层(<see cref="HtmlScriptExecutor"/>) 与 L3 生命周期层(<see cref="HtmlScriptHost"/>)；
    /// 本控件仅保留就绪查询核心（被 <see cref="IHtmlChannel.IsBridgeReadyAsync"/> 调用，供宿主层复用）。</summary>
    private async Task<bool> IsBridgeReadyCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (CoreWebView2 is null) return false;
        try
        {
            // 就绪断言用 document 级标志（对齐 BridgeScript）：WebView2 NavigateToString 重渲染替换 document 保留 window，
            // window 级标志会误判「桥已注入」导致新 document 缺点击监听器（[data-cmd] 点击转 postMessage 失效）；document 级保证重渲染后重新注入。
            var raw = await CoreWebView2.ExecuteScriptAsync("(!!document.__domBridgeInstalled);");
            return raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>回流链路自检（<see cref="IHtmlChannel.LoopbackProbeAsync"/>）：注入 __loopback postMessage 并等 <see cref="ProcessMessage"/> 收到同一 nonce。
    /// 直接证明「DOM postMessage → OnWebMessageReceived → 消息桥入口」闭环，不依赖可选诊断 sink（P0，消除「无日志=未达 C#」伪证）。
    /// 整个探针（含 EnsureCoreWebView2Async）封送 UI 线程，兼容脚本/工作线程调用。</summary>
    public Task<bool> LoopbackProbeAsync(CancellationToken ct = default)
        => RunOnUiThreadAsync(c => new ValueTask<bool>(LoopbackProbeCoreAsync(c)), ct);

    private async Task<bool> LoopbackProbeCoreAsync(CancellationToken ct)
    {
        // 自愈重试（P0/阶段0）：装配时序/导航竞态会让「DOM postMessage → C# WebMessageReceived」瞬时断裂
        // （WebView2 首次初始化未稳、导航进行中、消息桥订阅随 CoreWebView2 重建丢失）。
        // 每次尝试前经 EnsureReadyAsync → EnsureMessageBridgeInstalled（首次 / 重建后重新订阅消息桥），
        // 从源头消除「postMessage 已发出、C# 订阅随旧实例丢失 → 回流断裂」缺陷，而非只探测失败。
        // ★ 降噪：各次瞬态失败先归拢本地——自愈成功属预期路径，不再逐条回显（终态「通过」由调用方一行确认）；
        //   仅终态失败时把归拢的失败细节整块上报，保证诊断数据不因降噪而丢。
        var transientNotes = new List<string>();
        for (var attempt = 1; attempt <= LoopbackProbeAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await EnsureReadyAsync(ct).ConfigureAwait(false);
                if (CoreWebView2 is null)
                {
                    if (attempt < LoopbackProbeAttempts)
                    {
                        await Task.Delay(LoopbackProbeDelayMs, ct).ConfigureAwait(false);
                        continue;
                    }
                    break; // 终态仍无 CoreWebView2 → 失败（下方统一上报）
                }

                var nonce = "lb-" + Interlocked.Increment(ref _loopbackSeq) + "-" + Guid.NewGuid().ToString("N");
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _loopbackWaiters[nonce] = tcs;
                try
                {
                    var js = "window.chrome.webview.postMessage(JSON.stringify({cmd:'__loopback__',nonce:'" + nonce + "'}));";
                    var exec = await ExecuteRootScriptCoreAsync(js, ct).ConfigureAwait(false);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var done = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token)).ConfigureAwait(false);
                    if (done == tcs.Task && tcs.Task.IsCompletedSuccessfully)
                        return true; // 成功：瞬时失败不占行，终态确认由调用方统一输出
                    transientNotes.Add($"回流自检尝试 {attempt}/{LoopbackProbeAttempts} 未达 C# 消息桥（ExecuteScriptAsync 返回={TextUtil.Truncate(exec ?? string.Empty)}）");
                }
                finally
                {
                    _loopbackWaiters.TryRemove(nonce, out _);
                }
            }
            catch (Exception ex)
            {
                if (attempt == LoopbackProbeAttempts) break; // 终态异常 → 失败（下方统一上报）
                transientNotes.Add($"回流自检异常 {attempt}/{LoopbackProbeAttempts}: {ex.GetType().Name}: {ex.Message}");
            }

            if (attempt < LoopbackProbeAttempts)
                await Task.Delay(LoopbackProbeDelayMs, ct).ConfigureAwait(false);
        }

        // 终态失败：把各次瞬态失败整块上报（保留失败定位所需细节）
        foreach (var note in transientNotes)
            ReportDiagnostics("HtmlChannel_loopback", note);
        return false;
    }

    /// <summary>桥链路结构化诊断上报（经 <see cref="BridgeDiagnostics"/> 出口；未接线时兜底到 Trace，保证关键断点不因 sink 缺失而静默）。</summary>
    private void ReportDiagnostics(string category, string message)
    {
        BridgeDiagnostics?.Invoke(category, message);
        System.Diagnostics.Trace.WriteLine($"[{category}] {message}");
    }

    /// <summary>
    /// 真实鼠标点击（Win32 SendInput，isTrusted=true）：经 JS 读元素中心 → CSS 像素 × DPI 缩放换算控件客户区设备像素 →
    /// ClientToScreen 转屏幕坐标 → SendInput 发送真实左键移动+按下+抬起。与合成 dispatchEvent（isTrusted=false）不同，
    /// 走完整真实输入管线（命中测试/焦点/事件冒泡），用于验证真实手动点击链路（脚本 realclick 命令）。
    /// </summary>
    private async Task<string> RealClickCoreAsync(string name, CancellationToken ct)
    {
        // L2 执行层渲染真实点击坐标查询模板（参数 JSON 序列化注入，防引号转义漂移）；NOT_FOUND 附带文档诊断
        // （现存 data-name 列表/body 长度/加载状态/href），定位「真实 document 未渲染目标元素」vs「命名/选择器问题」。
        var rectJs = _scriptExecutor.RenderRectQuery(name);
        var raw = await CoreWebView2.ExecuteScriptAsync(rectJs);
        // ExecuteScriptAsync 返回 JSON 字符串字面量（"..."），解包后再解析对象
        var json = HtmlElementNode.UnwrapString(raw);
        if (json == "NOT_VISIBLE")
            return json;
        if (json is not null && json.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            return json; // 含诊断后缀（names/body/state/href）
        if (string.IsNullOrWhiteSpace(json))
            return json;

        double cx, cy, dpr;
        try
        {
            using var doc = JsonDocument.Parse(json);
            cx = doc.RootElement.GetProperty("x").GetDouble();
            cy = doc.RootElement.GetProperty("y").GetDouble();
            dpr = doc.RootElement.GetProperty("dpr").GetDouble();
        }
        catch (JsonException)
        {
            return "PARSE_ERROR";
        }

        // ① 激活/聚焦宿主窗体与控件：SendInput 需目标窗口命中；未激活时 Chromium 把首个点击仅用于激活窗口，不产生 click。
        ActivateForRealInput();

        // ② 精确坐标：设备像素 = CSS 像素 × devicePixelRatio（WebView2 真实 DPI/缩放，优于 DeviceDpi 近似）；
        //    viewport 原点 = 控件客户区原点，再 ClientToScreen 转屏幕坐标。
        var scale = Math.Max(0.25, dpr);
        var pt = new NativePoint((int)(cx * scale), (int)(cy * scale));
        if (IsHandleCreated)
            ClientToScreen(Handle, ref pt);

        var screenW = GetSystemMetrics(SM_CXSCREEN);
        var screenH = GetSystemMetrics(SM_CYSCREEN);
        var absX = (int)((ulong)pt.X * 65535 / (ulong)Math.Max(1, screenW - 1));
        var absY = (int)((ulong)pt.Y * 65535 / (ulong)Math.Max(1, screenH - 1));

        // ③ Chromium 输入管线需先命中（move，给足延时让 hover/命中测试完成），再 down/up 才产生真实 click。
        // 用 await Task.Delay 而非 Thread.Sleep：本方法已在 WebView2 的 UI 线程执行，Thread.Sleep 会阻塞整条 UI 线程
        // （渲染/重绘卡死）；Task.Delay 非阻塞释放 UI 线程、保持同等时序。
        SendMouseInput(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE, absX, absY);
        await Task.Delay(80, ct);
        SendMouseInput(MOUSEEVENTF_LEFTDOWN, 0, 0);
        await Task.Delay(50, ct);
        SendMouseInput(MOUSEEVENTF_LEFTUP, 0, 0);
        return "OK";
    }

    /// <summary>激活宿主窗体并聚焦 WebView2 控件，确保 SendInput 真实点击命中目标窗口（而非仅激活窗口）。UI 线程调用。</summary>
    private void ActivateForRealInput()
    {
        if (!IsHandleCreated)
            return;
        try
        {
            if (!Focused)
                this.Focus(); // 让 WebView2 获得输入焦点（Chromium 聚焦）
        }
        catch { }

        try
        {
            var form = FindForm();
            if (form is not null)
            {
                if (form.WindowState == FormWindowState.Minimized)
                    form.WindowState = FormWindowState.Normal;
                if (!form.Focused)
                    form.Activate();
                form.BringToFront();
            }
        }
        catch { }
    }

    private static void SendMouseInput(uint flags, int absX, int absY)
    {
        var input = new NativeInput { Type = INPUT_MOUSE };
        input.Mouse.dx = absX;
        input.Mouse.dy = absY;
        input.Mouse.dwFlags = flags;
        SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeMouseInput Mouse;
    }

    private const uint INPUT_MOUSE = 0;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    // IHtmlChannel 显式实现：整页渲染 + 页内 DOM 指令统一入口（CoreWebView2 访问收敛到本控件唯一封送点）。
    // P4 写回收敛：消息桥重渲染写回（hostWriter → RenderHtmlAsync）导航后等待点击桥就绪，确保重渲染产物（含 data-cmd/data-name
    // 交互元素）落地到存活 document 才返回——消除「写回 fire-and-forget → 后续脚本点击命中旧 DOM → task-*/viewType 点击无效/NOT_FOUND」缺陷。
    async Task IHtmlChannel.RenderHtmlAsync(string html, CancellationToken ct)
    {
        await SetHtmlAsync(html, ct).ConfigureAwait(false);
        if (!await ConvergeBridgeAsync(ct).ConfigureAwait(false))
            ReportDiagnostics("HtmlChannel_writeback_not_converged", "RenderHtmlAsync 写回后点击桥未就绪（重渲染产物可能未落地）。");
    }

    // 收敛写已上移连接层 FrontEndDriven（ObservedHtmlChannel 装饰器 = ObserveSessionManager + WebView2Transport 适配本通道），
    // 本控件不再内建 Observe 依赖（DIP 剥离）；如需收敛写，由组合根包一层 IObservedHtmlChannel 后使用。
    Task<string> IHtmlChannel.ExecuteAsync(string op, string name, string? value, CancellationToken ct)
        => ExecuteDomAsync(op, name, value, ct);

    Task<bool> IHtmlChannel.IsBridgeReadyAsync(CancellationToken ct)
        => RunOnUiThreadAsync(c => new ValueTask<bool>(IsBridgeReadyCoreAsync(c)), ct);

    /// <summary>原生脚本执行通道（让 L2 执行层 / L3 生命周期层的模板展开与桥注入复用本控件唯一 WebView2 封送点）。
    /// 就绪前 / 无 CoreWebView2 时返回空串；返回页面执行结果原文。后台线程调用自动封送 UI 线程。</summary>
    private async Task<string> ExecuteRootScriptCoreAsync(string javaScript, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);
        if (CoreWebView2 is null) return string.Empty;
        return await CoreWebView2.ExecuteScriptAsync(javaScript);
    }

    Task<string> IHtmlChannel.ExecuteScriptAsync(string javaScript, CancellationToken ct)
        => RunOnUiThreadAsync(c => new ValueTask<string>(ExecuteRootScriptCoreAsync(javaScript, c)), ct);
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? json = null;
        try { json = e.TryGetWebMessageAsString(); }
        catch { json = null; }
        if (string.IsNullOrWhiteSpace(json))
        {
            // 点击桥（__domBridge）以「对象」形态 postMessage({cmd,...})：TryGetWebMessageAsString 对非字符串返回 null，
            // 若不兜底则对象形态消息被静默丢弃 → [data-cmd] 点击回流断裂（回流探针用 JSON.stringify 字符串形态，故探针正常而点击失效，
            // 8.26 实证）。WebMessageAsJson 对对象/字符串均返回 JSON 表示，作统一兜底。
            try { json = e.WebMessageAsJson; }
            catch { json = null; }
        }
        if (string.IsNullOrWhiteSpace(json))
            return;

        // WebView2 回调线程非 UI 线程：marshal 回 UI 线程再触碰控件状态/触发事件。
        var captured = json;
        if (IsDisposed || !IsHandleCreated)
        {
            ProcessMessage(captured);
            return;
        }
        try { BeginInvoke(new Action(() => ProcessMessage(captured))); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

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
        if (LastMessagePayload is not null
            && LastMessagePayload.TryGetValue("cmd", out var cmd)
            && cmd == "__loopback__"
            && LastMessagePayload.TryGetValue("nonce", out var nonce)
            && _loopbackWaiters.TryGetValue(nonce, out var lbTcs))
        {
            lbTcs.TrySetResult(nonce);
            return;
        }
        MessageReceived?.Invoke(this, new HtmlMessageReceivedEventArgs(json, LastMessagePayload));
    }
}
