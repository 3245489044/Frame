#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Abstractions;

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
public sealed partial class HtmlBackendRuntime : IUiRenderExecutor, IDisposable
{
    private readonly IHtmlChannel _channel;
    private readonly HtmlEventBridge.HtmlFunctionInvoker _invoker;
    private readonly HtmlEventBridge _eventBridge;
    private readonly HtmlScriptBridge _scriptBridge;
    private readonly IReadOnlyDictionary<string, ControlConfig> _controlsByName;
    /// <summary>宿主内容写回端口（DIP 剥离后的 Observe 收敛写抽象）：由连接层 FrontEndDriven 注入实现
    // （ObservedHtmlChannel 内部经 ObserveSessionManager + WebView2Transport）；null → 走纯渲染（无收敛语义）。
    private readonly HtmlWritePump? _writePump;

    /// <summary>写去重账本（唯一状态源）：宿主注入 <see cref="IWriteArbiter"/> 时由其持版本/指纹，
    /// 否则本地记账；四条写路径（host/region/tree/stream）共用同一准入与提交入口。</summary>
    private readonly WriteLedger _ledger;

    /// <summary>内容指纹单源（P3）：缺省 plate 内置 <see cref="UiFingerprint.Of"/>；宿主可注入框架指纹
    /// （<c>Strategy.Refresh.Ports.IFingerprint</c>）使领域与写落地共用同一口径。</summary>
    private readonly Func<string, string> _fingerprint;

    /// <summary>刷新追踪号工厂（必填）：唯一实现由上层注入 <c>Strategy.Refresh.Diagnostics.RefreshTraceId.New</c>，
    /// 使一次刷新贯穿 push→page→dispatch→sink 用同一来源（Plate 不持有第二个序号空间）。</summary>
    private readonly Func<string, string> _traceFactory;

    /// <summary>写回运行时开关与预算（B5-1；宿主未注入 ⇒ <see cref="UiWriteRuntimeOptions.Default"/>）。</summary>
    private readonly UiWriteRuntimeOptions _options;

    /// <summary>写回指标（B5-2；单源只读埋点，宿主导出）。</summary>
    private readonly UiWriteMetrics _metrics;

    /// <summary>上次指标上报时刻（60s 限流单源）。</summary>
    private long _lastMetricsReportMs;

    /// <summary>键控树写端口（L2 HTML 实现；经通道执行 <c>__uiReconcile.applyTree</c>）。</summary>
    private readonly HtmlTreeWriter _treeWriter;

    // 已装配的宿主 htmlviewer 消息订阅（控件名 → 订阅；幂等，重复 AttachHostMessage 先解除旧订阅）
    private readonly Dictionary<string, IDisposable> _hostSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    private static string HostSlot(string name) => "host:" + name;
    private static string RegionSlot(string name) => "region:" + name;
    private static string TreeSlot(string name) => "tree:" + name;

    /// <summary>流槽（append-only 面：日志 / 过程帧 / 气泡）：与 region/tree 同属写去重命名空间，前缀隔离不串。</summary>
    private static string StreamSlot(string name) => "stream:" + name;

    /// <summary>已完成整页渲染（文档壳 + 主题 CSS 就位）的宿主控件：首次写回必须整页渲染（补丁只换 body，头/主题需先落地）。</summary>
    private readonly ConcurrentDictionary<string, byte> _navigatedHosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>写回方式声明值（ControlConfig.Writeback）→ 补丁模式。</summary>
    private const string PatchWritebackValue = "patch";

    public HtmlBackendRuntime(
        IHtmlChannel channel,
        IReadOnlyDictionary<string, ControlConfig> declaration,
        HtmlEventBridge.HtmlFunctionInvoker invoker,
        HtmlWritePump? writePump = null,
        IWriteArbiter? writeArbiter = null,
        Func<string, string>? fingerprint = null,
        Func<string, string>? traceFactory = null,
        UiWriteRuntimeOptions? options = null,
        UiWriteMetrics? metrics = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlsByName = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _writePump = writePump;
        _ledger = new WriteLedger(writeArbiter);
        _fingerprint = fingerprint ?? UiFingerprint.Of;
        _traceFactory = traceFactory ?? throw new ArgumentNullException(nameof(traceFactory));
        _options = options ?? UiWriteRuntimeOptions.Default;
        _metrics = metrics ?? new UiWriteMetrics();
        _lastMetricsReportMs = Environment.TickCount64;
        _treeWriter = new HtmlTreeWriter(channel) { Diagnostics = Report };
        Readiness = new HtmlRuntimeReadinessReport();
        _eventBridge = new HtmlEventBridge(declaration, channel, invoker);
        // P0：事件桥的计划产物（RenderOutput.Plan）经本运行时（写协调器）单点落地——领域侧只产产物、不触通道。
        _eventBridge.RenderExecutor = this;
        // 脚本桥与事件桥共享同一通道；exec 端口取文本叙述（计划产物不适用脚本 exec，单一实例避免重复 new）。
        _scriptBridge = new HtmlScriptBridge(channel,
            async (name, parameters) => (await invoker(name, parameters).ConfigureAwait(false)).Text ?? string.Empty);
        // 聚合事件桥链路诊断到本运行时统一出口（宿主接线到日志门面）。
        _eventBridge.Diagnostics += Report;
    }

    public IHtmlChannel Channel => _channel;

    /// <summary>
    /// 消息调度器（F1，可选）：装配后页面消息只在 UI 线程入队（O(1)），
    /// 取数/渲染在 worker 串行执行；DOM 落地仍经 <see cref="IHtmlChannel"/> 单点封送。
    /// null ⇒ 维持「UI 线程直接执行」旧路径（零回归）。
    /// </summary>
    public IWebMessageScheduler? MessageScheduler { get; set; }

    /// <summary>写回运行时开关/预算（宿主只读视图）。</summary>
    public UiWriteRuntimeOptions Options => _options;

    /// <summary>写回指标（单源只读；宿主可导出为日志/面板）。</summary>
    public UiWriteMetrics Metrics => _metrics;

    /// <summary>IUiRenderExecutor：执行写回计划（单一 DOM 写落地入口）。事件桥/业务侧产出的
    /// <see cref="RenderOutput.Plan"/> 一律经此落地（P0 单写者）。<paramref name="source"/> 为写回来源（必填）：
    /// 与 <see cref="UiWritePlan.Source"/> 不一致时显式记异常（来源漂移可见化，不静默）。</summary>
    public Task<UiWriteResult> ExecuteAsync(UiWritePlan plan, UiWriteSource source, string? traceId = null, CancellationToken ct = default)
    {
        if (plan is not null && plan.Source != source && source != UiWriteSource.Mount)
        {
            Report("EventBridge_writeback",
                $"来源漂移 plan.Source={plan.Source} 调用方 source={source} kind={plan.Kind}（按调用方来源落地）");
        }

        // 调用方来源是唯一裁决面：不得回落 plan.Source（否则 Refresh/Interactive 被计划自述覆盖）。
        return WriteAsync(plan, source, traceId, ct);
    }

    /// <summary>装配就绪报告：静态项由组合根填充，消息订阅项由 <see cref="AttachHostMessage"/> 更新。</summary>
    public HtmlRuntimeReadinessReport Readiness { get; }

    private readonly DiagnosticPort _diagnostics = new();

    /// <summary>消息桥链统一诊断出口（category, message；宿主接线到日志门面，未接线为空操作）。
    /// 汇总通道（<see cref="IHtmlChannel"/>）webmessage 埋点 + 事件桥链路（route/invoke/writeback）埋点。</summary>
    public Action<string, string>? Diagnostics { get => _diagnostics.Sink; set => _diagnostics.Sink = value; }

    private void Report(string category, string message) => _diagnostics.Report(category, message);

    // F1 单飞合并：每控件至多一个在飞写；在飞期间的重入/并发写按来源优先级合并（latest-wins），
    // 使「写回↔消息桥成环」在结构上无法累积深度/并发。
    private readonly ControlSerialQueue _writeQueue = new();

    /// <summary>脚本桥命令协议 set/get/click/exec/sleep 落到 HTML 原生元素——运行时唯一实例。</summary>
    public HtmlScriptBridge ScriptBridge => _scriptBridge;

    /// <summary>注册函数调用端口（事件桥/脚本桥共用）。</summary>
    public HtmlEventBridge.HtmlFunctionInvoker Invoker => _invoker;

    public Task<string> HandleEventAsync(string payload, CancellationToken ct = default)
        => _eventBridge.HandleAsync(payload, ct);

    /// <summary>消息路由执行体（F1 worker / 旧路径共用）：异常统一诊断，不向页面回抛。</summary>
    private async Task HandleMessageAsync(string hostControlName, string json, CancellationToken ct)
    {
        try
        {
            // 单协议路由中枢：data-cmd="event:{name}" 已由点击桥翻译回事件负载（含 event+name）→ 页内声明事件 HandleAsync；
            // 否则（cmd 消息桥 selectTask/selectType 等）→ 宿主 events.message 路由。
            var (eventName, controlName, _) = HtmlEventBridge.ParsePayload(json);
            if (!string.IsNullOrWhiteSpace(eventName) && !string.IsNullOrWhiteSpace(controlName))
                await _eventBridge.HandleAsync(json, ct).ConfigureAwait(false);
            else
                await _eventBridge.HandleHostMessageAsync(hostControlName, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 收敛静默 catch：记录错误分类 + 上下文（仍不向页面回抛，但必须有诊断出口）。
            Report("HtmlChannel_webmessage_error", $"消息路由异常: {ex.GetType().Name}: {ex.Message}, 负载={TextUtil.Truncate(json)}");
        }
    }

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

        // 域隔离解析（单一入口）：宿主控件 = host 域偏好（裸名 → host:）。
        if (!HtmlDeclarationParser.TryResolveControl(_controlsByName, hostControlName,
                HtmlDeclarationParser.ControlDomain.Host, out var control))
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
        // C2 单一写回入口：宿主写回闭包仅转发到 WriteHostContentAsync（哨兵/去重/补丁/收敛写/回退集中一处）。
        // 来源由事件桥显式给出（页面消息 = Interactive），禁止在此做 Mount 缺省。
        _eventBridge.RegisterHostWriter(hostControlName,
            (html, source) => WriteHostContentAsync(hostControlName, writeMode, contentSink, html, source));
        // D2 单写者：值契约（ECS 输出等）的 DOM 落地统一委托本协调器；未挂接时控件回退整页渲染。
        if (channel is HtmlViewer hostViewer)
        {
            hostViewer.HostContentWriter = (html, source, ct) =>
                WriteHostContentAsync(hostControlName, writeMode, contentSink, html, source, ct);
            // B3 单写落地：携带写回计划的产物（ECS 等部门）经此计划执行器落地，替代对 ToString 的静默丢弃。
            hostViewer.RenderPlanExecutor = plan => ExecuteAsync(plan, plan.Source, plan.TraceId);
        }

        var handler = new EventHandler<HtmlMessageReceivedEventArgs>((_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e?.Json))
                return;
            var json = e.Json;
            Report("HtmlChannel_webmessage", $"收到 postMessage: {TextUtil.Truncate(json)}");

            // F1：UI 线程只投递（O(1)）；执行体在 RefreshWorker 内串行运行。
            // 未装配 scheduler / 循环探针等必须同步返回的消息 ⇒ 沿用旧直接路径（但必须可见化，禁静默）。
            if (MessageScheduler is not null)
            {
                if (MessageScheduler.TrySchedule(json, ct => HandleMessageAsync(hostControlName, json, ct)))
                    return;
                Report("HtmlChannel_webmessage",
                    $"调度器未接管（同步回投/关闭中）⇒ UI 线程就地执行: {TextUtil.Truncate(json)}");
            }
            else
            {
                Report("HtmlChannel_webmessage",
                    $"未装配 MessageScheduler ⇒ UI 线程就地执行（可能阻塞）: {TextUtil.Truncate(json)}");
            }

            _ = HandleMessageAsync(hostControlName, json, CancellationToken.None);
        });
        // D3 单入口（仲裁式，可撤销）：抢占 hostControlName 的消息订阅权——撤销他方旧 owner，顺序无关，
        // 消除「plate 桥 + ECS 事件绑定」对同一 MessageReceived 的双路调用（重复副作用与双写根因）。
        if (channel is HtmlViewer subscribedViewer)
        {
            _hostSubscriptions[hostControlName] = subscribedViewer.AcquireMessageChannel(hostControlName, handler);
        }
        else
        {
            channel.MessageReceived += handler;
            _hostSubscriptions[hostControlName] = new Subscription(channel, handler);
        }

        Readiness.HostWriterRegistered = true;
        Readiness.MessageSubscribed = true;
        return null;
    }

    /// <summary>解装配（B1 幂等装配 + 订阅所有权）：解订阅全部宿主消息——旧运行时被覆盖后其 handler 不得成孤儿仍触发。</summary>
    public void Dispose()
    {
        foreach (var kv in _hostSubscriptions)
            kv.Value.Dispose();
        _hostSubscriptions.Clear();
        (MessageScheduler as IDisposable)?.Dispose();
        MessageScheduler = null;
    }

    /// <summary>
    /// 宿主内容写回入口（F1 单飞合并）：空结果跳过；同控件在飞期间的重入/并发写**合并为最新一次**
    /// （按来源优先级，不递归）——使「写回↔消息桥成环」在结构上无法累积深度/并发。
    /// <para>去重只在唯一账本（<see cref="WriteLedger"/>）判定一次；入口不再持有第二套"上次内容"。</para>
    /// </summary>
    private Task WriteHostContentAsync(
        string hostControlName, HtmlWriteBackMode writeMode, Action<string>? contentSink, string html,
        UiWriteSource source = UiWriteSource.Mount, CancellationToken ct = default)
    {
        // L2 类型化：空结果 = 无内容写回（取代 NoWrite 哨兵串；调用方已按区域直接写回即返回空串）。
        if (string.IsNullOrEmpty(html))
        {
            Report("EventBridge_writeback", $"写回 {hostControlName} 跳过（空结果 = 无写回内容）");
            return Task.CompletedTask;
        }

        if (!_writeQueue.TryBegin(hostControlName, html, source, out var first))
        {
            // F1 单飞合并：在飞期间的重入/并发写 → 合入待写槽（按来源优先级，不递归、不触 DOM）。
            var counters = _writeQueue.TakeCounters(hostControlName);
            _metrics.OnCoalesced((int)Math.Max(1, counters.Coalesced));
            Report("EventBridge_writeback",
                $"写回 {hostControlName} 合并（在飞中 source={source}，来源优先级择优；dropped={counters.Dropped}" +
                (counters.DroppedPreview is not null ? $", droppedPreview={counters.DroppedPreview}" : string.Empty) + ")");
            return Task.CompletedTask;
        }
        return DrainHostWriteAsync(hostControlName, writeMode, contentSink, first, source, ct);
    }

    /// <summary>F1 单飞 drain：串行执行该控件写；执行期间到达的写请求被合并，末态以最新一并落盘。</summary>
    private async Task DrainHostWriteAsync(
        string hostControlName, HtmlWriteBackMode writeMode, Action<string>? contentSink,
        string firstHtml, UiWriteSource firstSource, CancellationToken ct)
    {
        var html = firstHtml;
        var source = firstSource;
        var drained = false;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await WriteHostContentCoreAsync(hostControlName, writeMode, contentSink, html, source, ct).ConfigureAwait(false);
                if (!_writeQueue.TryDequeue(hostControlName, out var next, out var nextSource))
                {
                    drained = true;
                    break;
                }
                html = next;
                source = nextSource;
            }
        }
        finally
        {
            // 正常 drain 已由 TryDequeue 释放；异常/取消路径兜底释放，且**不静默丢**待写内容。
            if (!drained)
            {
                var counters = _writeQueue.TakeCounters(hostControlName);
                var droppedPreview = _writeQueue.ForceExit(hostControlName);
                if (counters.Dropped > 0 || droppedPreview is not null)
                    Report("EventBridge_writeback",
                        $"写回 {hostControlName} 异常退出丢弃待写（replaced={counters.Dropped}, preview={droppedPreview ?? counters.DroppedPreview ?? "-"}）");
            }
        }
    }

    /// <summary>写回核心（单点执行，仅经 drain 串行调用）：去重 → 补丁/收敛写/纯渲染 → 记录去重。</summary>
    private async Task WriteHostContentCoreAsync(
        string hostControlName, HtmlWriteBackMode writeMode, Action<string>? contentSink,
        string html, UiWriteSource source, CancellationToken ct)
    {
        // F3 不变量断言（非抛出）：核心只应由 drain 在其控件在飞时调用——单飞即守卫。
        if (!_writeQueue.IsInFlight(hostControlName))
            Report("EventBridge_invariant", $"写回核心被直接调用（inFlight=false, site={hostControlName}）：单飞不变量被破坏");
        // L2 类型化：空结果 = 无内容写回（drain 已短路，此处兜底）。
        if (string.IsNullOrEmpty(html))
            return;
        var slotKey = HostSlot(hostControlName);
        var dedupKey = _fingerprint(html);
        if (!_ledger.TryAdmit(slotKey, dedupKey, out var version))
        {
            Report("EventBridge_writeback", $"写回 {hostControlName} 跳过（仲裁：未变/过期；mode={writeMode}）");
            return;
        }
        contentSink?.Invoke(html);
        if (writeMode == HtmlWriteBackMode.Patch && _navigatedHosts.ContainsKey(hostControlName))
        {
            var status = await ExecuteDomTypedAsync(DomOps.Reconcile, null, html, ct).ConfigureAwait(false);
            if (status == DomOpStatus.Ok)
            {
                _ledger.Commit(slotKey, version, dedupKey);
                Report("EventBridge_writeback", $"补丁写回 {hostControlName}: {DomOpStatus.Ok}（不重载文档，UI 位置保持）");
                return;
            }
            _ledger.MarkFailure(slotKey, dedupKey);
            // 断整页回退链：**已挂载**宿主补丁未生效 ⇒ 不整页重载（避免 document 重建丢展开/滚动/焦点），
            // 仅记诊断并保留现有 DOM；下一拍重试（reconcile 自愈会在后续调用补注入）。整页仅用于首挂载。
            Report("EventBridge_writeback",
                $"补丁写回 {hostControlName} 未生效（{status}）→ 保留现状（不整页回退，保护交互态）");
            return;
        }
        if (_writePump is not null)
        {
            var result = await _writePump(html, source, ct).ConfigureAwait(false);
            // D5：仅 Committed 记成功参与去重——Degraded（未收敛）不得被"确认"，否则下次同内容被误跳过。
            if (result.Status == HtmlWriteBackStatus.Committed) _ledger.Commit(slotKey, version, dedupKey);
            else _ledger.MarkFailure(slotKey, dedupKey);
            if (result.Status != HtmlWriteBackStatus.Failed)
                _navigatedHosts.TryAdd(hostControlName, 0);
            Report("EventBridge_writeback",
                $"收敛写回 {hostControlName}: result={result.Status} source={source}" +
                (result.Detail is not null ? $", ack={result.Detail}" : "") +
                (result.Window is not null ? $", window={result.Window.Value.TotalMilliseconds:F0}ms" : ""));
        }
        else
        {
            await _channel.RenderHtmlAsync(html, ct).ConfigureAwait(false);
            _ledger.Commit(slotKey, version, dedupKey);
            _navigatedHosts.TryAdd(hostControlName, 0);
            Report("EventBridge_writeback", $"写回 {hostControlName}（无收敛 pump，走纯渲染）");
        }
    }

    /// <summary>执行受控 DOM 指令并回串为类型化状态（单点：消除 <c>UnwrapString + Parse</c> 组合的多处重复）。</summary>
    private async Task<DomOpStatus> ExecuteDomTypedAsync(string op, string? name, string? value, CancellationToken ct)
        => DomOps.Parse(HtmlElementNode.UnwrapString(
            await _channel.ExecuteAsync(op, name, value, ct).ConfigureAwait(false)));

    /// <summary>按区域写回（I5 区域化写回，P2）：patch 具名区域容器（不重载文档）。
    /// 返回 true = 已落地成功；false = 未命中/未生效/内容未变（未触 DOM，调用方不得据此推断"内容已更新"）。</summary>
    public async Task<bool> WriteRegionAsync(string regionName, string html, CancellationToken ct = default)
    {
        var (status, admitted) = await WriteRegionCoreStatusAsync(regionName, html, ct).ConfigureAwait(false);
        return admitted && status == DomOpStatus.Ok;
    }


    /// <summary>写回方式解析（声明驱动，单一真源）：宿主控件声明 <c>writeback="patch"</c> → 内容补丁；缺省 navigate（整页重载，零回归）。
    /// 域隔离同事件桥：先裸名、再 host: 域键。</summary>
    private HtmlWriteBackMode ResolveWriteBackMode(string hostControlName)
    {
        if (HtmlDeclarationParser.TryResolveControl(_controlsByName, hostControlName,
                HtmlDeclarationParser.ControlDomain.Host, out var cfg))
        {
            if (string.Equals(cfg!.Writeback?.Trim(), PatchWritebackValue, StringComparison.OrdinalIgnoreCase))
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

