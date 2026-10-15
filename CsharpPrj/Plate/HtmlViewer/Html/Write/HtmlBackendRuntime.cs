#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
public sealed class HtmlBackendRuntime : IUiRenderExecutor, IDisposable
{
    private readonly IHtmlChannel _channel;
    private readonly HtmlEventBridge.HtmlFunctionInvoker _invoker;
    private readonly HtmlEventBridge _eventBridge;
    private readonly HtmlScriptBridge _scriptBridge;
    private readonly IReadOnlyDictionary<string, ControlConfig> _controlsByName;
    /// <summary>宿主内容写回端口（DIP 剥离后的 Observe 收敛写抽象）：由连接层 FrontEndDriven 注入实现
    // （ObservedHtmlChannel 内部经 ObserveSessionManager + WebView2Transport）；null → 走纯渲染（无收敛语义）。
    private readonly HtmlWritePump? _writePump;

    /// <summary>写仲裁端口（L0，宿主注入；DIP：plate 只依赖抽象）：每 slot 单调版本 + baseline 过期即丢 + 指纹去重。
    /// null → 回退旧 <see cref="_lastWrite"/> 内容比对（零回归）。</summary>
    private readonly IWriteArbiter? _writeArbiter;

    /// <summary>内容指纹单源（P3）：缺省 plate 内置 <see cref="UiFingerprint.Of"/>；宿主可注入框架指纹
    /// （<c>Strategy.Refresh.Ports.IFingerprint</c>）使领域与写落地共用同一口径。</summary>
    private readonly Func<string, string> _fingerprint;

    /// <summary>刷新追踪号工厂（P4）：缺省 plate 本地序 <see cref="RefreshTrace.New"/>；宿主可注入框架
    /// <c>Strategy.Refresh.Diagnostics.RefreshTraceId.New</c>，使一次刷新贯穿 push→page→dispatch→sink 用同一来源。</summary>
    private readonly Func<string, string> _traceFactory;

    /// <summary>键控树写端口（L2 HTML 实现；经通道执行 <c>__uiReconcile.applyTree</c>）。</summary>
    private readonly HtmlTreeWriter _treeWriter;

    // 已装配的宿主 htmlviewer 消息订阅（控件名 → 订阅；幂等，重复 AttachHostMessage 先解除旧订阅）
    private readonly Dictionary<string, IDisposable> _hostSubscriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>写回去重账（D7 键空间隔离：host/region 以不同前缀键共用一个字典——共用一份逻辑、命名空间不串）。
    /// 内容未变则跳过，消除 1s 节流下的空转 DOM 抖动。</summary>
    private readonly Dictionary<string, WriteRecord> _lastWrite = new(StringComparer.OrdinalIgnoreCase);

    private static string HostSlot(string name) => "host:" + name;
    private static string RegionSlot(string name) => "region:" + name;
    private static string TreeSlot(string name) => "tree:" + name;

    /// <summary>已完成整页渲染（文档壳 + 主题 CSS 就位）的宿主控件：首次写回必须整页渲染（补丁只换 body，头/主题需先落地）。</summary>
    private readonly HashSet<string> _navigatedHosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct WriteRecord(string Html, bool Ok);

    /// <summary>写回方式声明值（ControlConfig.Writeback）→ 补丁模式。</summary>
    private const string PatchWritebackValue = "patch";

    public HtmlBackendRuntime(
        IHtmlChannel channel,
        IReadOnlyDictionary<string, ControlConfig> declaration,
        HtmlEventBridge.HtmlFunctionInvoker invoker,
        HtmlWritePump? writePump = null,
        IWriteArbiter? writeArbiter = null,
        Func<string, string>? fingerprint = null,
        Func<string, string>? traceFactory = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _controlsByName = declaration ?? throw new ArgumentNullException(nameof(declaration));
        _writePump = writePump;
        _writeArbiter = writeArbiter;
        _fingerprint = fingerprint ?? UiFingerprint.Of;
        _traceFactory = traceFactory ?? RefreshTrace.New;
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

    /// <summary>IUiRenderExecutor：执行写回计划（单一 DOM 写落地入口）。事件桥/业务侧产出的
    /// <see cref="RenderOutput.Plan"/> 一律经此落地（P0 单写者）。</summary>
    public Task<UiWriteResult> ExecuteAsync(UiWritePlan plan, string? traceId = null, CancellationToken ct = default)
        => WriteAsync(plan, traceId, ct);

    /// <summary>装配就绪报告：静态项由组合根填充，消息订阅项由 <see cref="AttachHostMessage"/> 更新。</summary>
    public HtmlRuntimeReadinessReport Readiness { get; }

    private readonly DiagnosticPort _diagnostics = new();

    /// <summary>消息桥链统一诊断出口（category, message；宿主接线到日志门面，未接线为空操作）。
    /// 汇总通道（<see cref="IHtmlChannel"/>）webmessage 埋点 + 事件桥链路（route/invoke/writeback）埋点。</summary>
    public Action<string, string>? Diagnostics { get => _diagnostics.Sink; set => _diagnostics.Sink = value; }

    private void Report(string category, string message) => _diagnostics.Report(category, message);

    // F1 单飞合并：每站点至多一个在飞写；在飞期间的重入/并发写合并为最新一次（latest-wins），
    // 使「写回↔消息桥成环」在结构上无法累积深度/并发（F3：单飞即守卫，删除重复的全局深度计数）。
    private readonly ConcurrentDictionary<string, WriteSlot> _writeSlots = new(StringComparer.OrdinalIgnoreCase);

    private sealed class WriteSlot
    {
        private readonly object _gate = new();
        public bool InFlight;
        public string? Pending;
        public bool HasPending;

        /// <summary>尝试进入写（首入者得 first）；在飞则合入待写槽并返回 false（不递归）。</summary>
        public bool TryBegin(string html, out string first)
        {
            lock (_gate)
            {
                if (InFlight)
                {
                    Pending = html;
                    HasPending = true;
                    first = html;
                    return false;
                }
                InFlight = true;
                first = html;
                return true;
            }
        }

        /// <summary>取下一待写（有则继续 drain）；无则将站点置回空闲并返回 false。</summary>
        public bool TryDequeue(out string next)
        {
            lock (_gate)
            {
                if (HasPending && Pending is not null)
                {
                    next = Pending;
                    Pending = null;
                    HasPending = false;
                    return true;
                }
                InFlight = false;
                next = string.Empty;
                return false;
            }
        }

        /// <summary>异常/收尾兜底：无条件置回空闲（幂等）。</summary>
        public void ForceExit()
        {
            lock (_gate)
            {
                InFlight = false;
                Pending = null;
                HasPending = false;
            }
        }
    }

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
        _eventBridge.RegisterHostWriter(hostControlName,
            html => WriteHostContentAsync(hostControlName, writeMode, contentSink, html));
        // D2 单写者：值契约（ECS 输出等）的 DOM 落地统一委托本协调器；未挂接时控件回退整页渲染。
        if (channel is HtmlViewer hostViewer)
        {
            hostViewer.HostContentWriter = html => WriteHostContentAsync(hostControlName, writeMode, contentSink, html);
            // B3 单写落地：携带写回计划的产物（ECS 等部门）经此计划执行器落地，替代对 ToString 的静默丢弃。
            hostViewer.RenderPlanExecutor = plan => ExecuteAsync(plan, plan.TraceId);
        }

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
    }

    /// <summary>
    /// 宿主内容写回入口（F1 单飞合并）：空结果跳过；同站点在飞期间的重入/并发写**合并为最新一次**
    /// （latest-wins，不递归）——使「写回↔消息桥成环」在结构上无法累积深度/并发。
    /// </summary>
    private Task WriteHostContentAsync(
        string hostControlName, HtmlWriteBackMode writeMode, Action<string>? contentSink, string html)
    {
        // L2 类型化：空结果 = 无内容写回（取代 NoWrite 哨兵串；调用方已按区域直接写回即返回空串）。
        if (string.IsNullOrEmpty(html))
        {
            Report("EventBridge_writeback", $"写回 {hostControlName} 跳过（空结果 = 无写回内容）");
            return Task.CompletedTask;
        }

        var slot = _writeSlots.GetOrAdd(hostControlName, _ => new WriteSlot());
        if (!slot.TryBegin(html, out var first))
        {
            // F1 单飞合并：在飞期间的重入/并发写 → 合入待写槽（latest-wins），不递归、不触 DOM。
            Report("EventBridge_writeback", $"写回 {hostControlName} 合并（在飞中，latest-wins）");
            return Task.CompletedTask;
        }
        return DrainHostWriteAsync(hostControlName, writeMode, contentSink, slot, first);
    }

    /// <summary>F1 单飞 drain：串行执行该站点写；执行期间到达的写请求被合并，末态以最新一并落盘。</summary>
    private async Task DrainHostWriteAsync(
        string hostControlName, HtmlWriteBackMode writeMode, Action<string>? contentSink, WriteSlot slot, string firstHtml)
    {
        var html = firstHtml;
        var drained = false;
        try
        {
            while (true)
            {
                await WriteHostContentCoreAsync(hostControlName, writeMode, contentSink, slot, html).ConfigureAwait(false);
                if (!slot.TryDequeue(out var next))
                {
                    drained = true;
                    break;
                }
                html = next;
            }
        }
        finally
        {
            // 正常 drain 已由 TryDequeue 释放槽位；异常路径兜底释放，避免站点永久占位。
            if (!drained) slot.ForceExit();
        }
    }

    /// <summary>写回核心（单点执行，仅经 drain 串行调用）：去重 → 补丁/收敛写/纯渲染 → 记录去重。</summary>
    private async Task WriteHostContentCoreAsync(
        string hostControlName, HtmlWriteBackMode writeMode, Action<string>? contentSink, WriteSlot slot, string html)
    {
        // F3 不变量断言（非抛出）：核心只应由 drain 在其站点在飞时调用——单飞即守卫，取代旧的全局深度熔断。
        if (!slot.InFlight)
            Report("EventBridge_invariant", $"写回核心被直接调用（slot.InFlight=false, site={hostControlName}）：单飞不变量被破坏");
        // L2 类型化：空结果 = 无内容写回（drain 已短路，此处兜底）。
        if (string.IsNullOrEmpty(html))
            return;
        var slotKey = HostSlot(hostControlName);
        var dedupKey = _fingerprint(html);
        if (!TryAdmit(slotKey, dedupKey, out var version))
        {
            Report("EventBridge_writeback", $"写回 {hostControlName} 跳过（仲裁：未变/过期；mode={writeMode}）");
            return;
        }
        contentSink?.Invoke(html);
        if (writeMode == HtmlWriteBackMode.Patch && _navigatedHosts.Contains(hostControlName))
        {
            var status = await ExecuteDomTypedAsync(DomOps.Reconcile, null, html, CancellationToken.None).ConfigureAwait(false);
            if (status == DomOpStatus.Ok)
            {
                RecordWrite(slotKey, version, dedupKey, true);
                Report("EventBridge_writeback", $"补丁写回 {hostControlName}: {DomOpStatus.Ok}（不重载文档，UI 位置保持）");
                return;
            }
            // 断整页回退链：**已挂载**宿主补丁未生效 ⇒ 不整页重载（避免 document 重建丢展开/滚动/焦点），
            // 仅记诊断并保留现有 DOM；下一拍重试（reconcile 自愈会在后续调用补注入）。整页仅用于首挂载。
            Report("EventBridge_writeback",
                $"补丁写回 {hostControlName} 未生效（{status}）→ 保留现状（不整页回退，保护交互态）");
            return;
        }
        if (_writePump is not null)
        {
            var result = await _writePump(html).ConfigureAwait(false);
            // D5：仅 Committed 记成功参与去重——Degraded（未收敛）不得被"确认"，否则下次同内容被误跳过。
            RecordWrite(slotKey, version, dedupKey, result.Status == HtmlWriteBackStatus.Committed);
            if (result.Status != HtmlWriteBackStatus.Failed)
                _navigatedHosts.Add(hostControlName);
            Report("EventBridge_writeback",
                $"收敛写回 {hostControlName}: result={result.Status}" +
                (result.Detail is not null ? $", ack={result.Detail}" : "") +
                (result.Window is not null ? $", window={result.Window.Value.TotalMilliseconds:F0}ms" : ""));
        }
        else
        {
            await _channel.RenderHtmlAsync(html).ConfigureAwait(false);
            RecordWrite(slotKey, version, dedupKey, true);
            _navigatedHosts.Add(hostControlName);
            Report("EventBridge_writeback", $"写回 {hostControlName}（无收敛 pump，走纯渲染）");
        }
    }

    /// <summary>执行受控 DOM 指令并回串为类型化状态（单点：消除 <c>UnwrapString + Parse</c> 组合的多处重复）。</summary>
    private async Task<DomOpStatus> ExecuteDomTypedAsync(string op, string? name, string? value, CancellationToken ct)
        => DomOps.Parse(HtmlElementNode.UnwrapString(
            await _channel.ExecuteAsync(op, name, value, ct).ConfigureAwait(false)));

    /// <summary>按区域写回（I5 区域化写回，P2）：patch 具名区域容器（<c>DomOps.Patch name=region-*</c>，不重载文档）；
    /// 内容未变按区域跳过（按区域去重）。返回 false = 未命中/补丁未生效（调用方据结果回退整页渲染）。</summary>
    public async Task<bool> WriteRegionAsync(string regionName, string html, CancellationToken ct = default)
        => await WriteRegionStatusAsync(regionName, html, ct).ConfigureAwait(false) == DomOpStatus.Ok;

    /// <summary>写准入（单一入口）：有仲裁器 → NextVersion + Decide（Stale/Skip 即拒，不触 DOM）；
    /// 无仲裁器 → 回退旧 <see cref="_lastWrite"/> 内容比对（零回归）。</summary>
    private bool TryAdmit(string slotKey, string dedupKey, out long version)
    {
        version = 0;
        if (_writeArbiter is not null)
        {
            version = _writeArbiter.NextVersion(slotKey);
            return _writeArbiter.Decide(slotKey, version, dedupKey) == WriteVerdict.Apply;
        }
        return !(_lastWrite.TryGetValue(slotKey, out var prev)
                 && prev.Ok && string.Equals(prev.Html, dedupKey, StringComparison.Ordinal));
    }

    /// <summary>写记账（单一入口）：仅落地成功（<paramref name="ok"/>）才推进仲裁 baseline。</summary>
    private void RecordWrite(string slotKey, long version, string dedupKey, bool ok)
    {
        _lastWrite[slotKey] = new WriteRecord(dedupKey, ok);
        if (ok) _writeArbiter?.Commit(slotKey, version, dedupKey);
    }

    /// <summary>按区域写回并返回**类型化状态**（供调用方定位失败分支：NotFound=区域容器缺失 / StateNotReady=补丁态模块未就绪）。</summary>
    public async Task<DomOpStatus> WriteRegionStatusAsync(string regionName, string html, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(regionName)) return DomOpStatus.Unknown;
        var slotKey = RegionSlot(regionName);
        var dedupKey = _fingerprint(html);
        if (!TryAdmit(slotKey, dedupKey, out var version))
        {
            Report("EventBridge_writeback", $"区域写回 {regionName} 跳过（仲裁：未变/过期，不触 DOM）");
            return DomOpStatus.Ok;
        }
        var parsed = await ExecuteDomTypedAsync(DomOps.Reconcile, regionName, html, ct).ConfigureAwait(false);
        if (parsed == DomOpStatus.Ok)
            RecordWrite(slotKey, version, dedupKey, true);
        Report("EventBridge_writeback", parsed == DomOpStatus.Ok
            ? $"区域写回 {regionName}: {DomOpStatus.Ok}（不重载文档，UI 位置保持）"
            : $"区域写回 {regionName} 未生效（{parsed}）");
        return parsed;
    }

    /// <summary>按叶子节点定点写回（S5/L3）：经受控 <c>DomOps.Set</c> 仅更新命中的单元素文本（如 elapsed 徽章），
    /// 不触发区域重渲染；用于心跳刷新只改易变叶子的场景（DOM 变更计数=1）。name 命中失败返回 false。
    /// <para><see cref="DomOps.Set"/> 语义：表单元素写 value，<c>.host</c> 写 innerHTML，其余写 textContent。</para></summary>
    public async Task<bool> WriteLeafAsync(string name, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var status = await ExecuteDomTypedAsync(DomOps.Set, name, value, ct).ConfigureAwait(false);
        var ok = status == DomOpStatus.Ok;
        Report("EventBridge_writeback", ok
            ? $"叶子写回 {name}: {DomOpStatus.Ok}"
            : $"叶子写回 {name} 未命中（{status}）");
        return ok;
    }

    /// <summary>
    /// 单一写回协调器入口（L2 / R3）：接收 <see cref="UiWritePlan"/> 独占执行区域/键控树/叶子/整文档写回，
    /// 逐目标独立判成败、产出 <see cref="UiWriteResult"/>（类型化显式策略，不再靠哨兵）。
    /// <para>Region/Leaf 目标名 = 最终 DOM 名（如 <c>region-tree</c>）；Region 默认经键控对齐原语落地。
    /// FullDocument 经 <see cref="HtmlWritePump"/> 收敛写或纯渲染（无宿主端口 = 显式 Failed，不静默吞）。</para>
    /// </summary>
    public Task<UiWriteResult> WriteAsync(UiWritePlan plan, CancellationToken ct = default)
        => WriteAsync(plan, null, ct);

    /// <summary>单一写回协调器入口（带刷新追踪号：一次刷新一条轨迹，<paramref name="traceId"/> 由刷新链路传入）。</summary>
    public async Task<UiWriteResult> WriteAsync(UiWritePlan plan, string? traceId, CancellationToken ct = default)
    {
        if (plan is null || plan.Kind == UiWriteKind.Skip)
            return new UiWriteResult(UiWriteStatus.Nothing, Array.Empty<string>(), Array.Empty<string>());

        var trace = plan.TraceId ?? traceId ?? _traceFactory("write");
        var written = new List<string>();
        var skipped = new List<string>();
        var anomalies = new List<string>();
        var anomalySeen = false;

        switch (plan.Kind)
        {
            case UiWriteKind.Region:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    var regionHtml = t.Payload as string ?? string.Empty;
                    if (await WriteRegionAsync(name, regionHtml, ct).ConfigureAwait(false)) written.Add(name);
                    else skipped.Add(name);
                }
                break;

            case UiWriteKind.Tree:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    if (t.Payload is not UiNode tree)
                    {
                        // 载荷类型不符可见化（治 E-6 静默）：本应恒为 UiNode，不符即产/传链漂移，显式上报而非静默 skip。
                        Report("EventBridge_writeback",
                            $"键控树写回 {name} 跳过：载荷类型={t.Payload?.GetType().Name ?? "<null>"}（期望 UiNode）trace={trace}");
                        skipped.Add(name);
                        continue;
                    }
                    // D-1 去重（与 Region 路径语义一致）：同目标 + 同树/同交互指纹 ⇒ 不触 DOM（Nothing，非失败）。
                    var treeFp = plan.Fingerprint ?? HtmlTreeWriter.Fingerprint(tree, plan.Interaction);
                    var treeSlotKey = TreeSlot(name);
                    if (!TryAdmit(treeSlotKey, treeFp, out var treeVersion))
                    {
                        Report("EventBridge_writeback", $"键控树写回 {name} 跳过（仲裁：未变/过期）trace={trace}");
                        skipped.Add(name);
                        continue;
                    }
                    var r = await _treeWriter.ReconcileAsync(name, tree, plan.Interaction, ct).ConfigureAwait(false);
                    // 键控对齐异常（重复键等）显式上报（I3：不静默；对齐结果类型化回传）。
                    if (r.Anomalies is { Count: > 0 })
                    {
                        anomalySeen = true;
                        anomalies.AddRange(r.Anomalies);
                        Report("Reconcile_anomaly",
                            $"键控对齐异常 target={name} trace={trace}: {string.Join(";", r.Anomalies)}");
                    }
                    // E-5：失败原因类型化可见化（区域缺失 vs 内核未就绪）——供上层定向自愈，不静默。
                    if (r.Status is UiWriteStatus.NotFound or UiWriteStatus.NotReady)
                        Report("EventBridge_writeback",
                            $"键控树写回 {name} 未落地（{r.Status}）trace={trace}");
                    // X-5：anomalies 非空 ⇒ Partial 且不计入 written（判据不合格 ⇒ ⊥，不放行）。
                    if ((r.Status is UiWriteStatus.Applied or UiWriteStatus.Partial) && r.Anomalies is not { Count: > 0 })
                    {
                        RecordWrite(treeSlotKey, treeVersion, treeFp, true);
                        written.Add(name);
                    }
                    else skipped.Add(name);
                }
                break;

            case UiWriteKind.Leaf:
                foreach (var t in plan.Targets)
                {
                    var name = t.Name ?? string.Empty;
                    var value = t.Payload as string ?? string.Empty;
                    if (await WriteLeafAsync(name, value, ct).ConfigureAwait(false)) written.Add(name);
                    else skipped.Add(name);
                }
                break;

            case UiWriteKind.FullDocument:
                var html = plan.Targets.Count > 0 ? plan.Targets[0].Payload as string ?? string.Empty : string.Empty;
                var status = await WriteFullPageAsync(html, ct).ConfigureAwait(false);
                if (status == HtmlWriteBackStatus.Failed)
                    return new UiWriteResult(UiWriteStatus.Failed, written, skipped, Fallback: "navigate", TraceId: trace);
                written.Add("body");
                break;
        }

        // D-1 聚合修正：仅 skipped（内容未变/去重）⇒ Skipped（非 Failed）；anomalySeen ⇒ 至少 Partial（不放行）。
        var result = written.Count == 0
            ? (skipped.Count == 0 ? UiWriteStatus.Nothing : UiWriteStatus.Skipped)
            : (skipped.Count == 0 && !anomalySeen ? UiWriteStatus.Applied : UiWriteStatus.Partial);
        Report("EventBridge_writeback",
            $"写回协调器 {plan.Kind} trace={trace}: {result}, written=[{string.Join(",", written)}], skipped=[{string.Join(",", skipped)}]{(anomalySeen ? ", anomalies=1" : string.Empty)}");
        return new UiWriteResult(result, written, skipped, TraceId: trace,
            Anomalies: anomalies.Count > 0 ? anomalies : null);
    }

    /// <summary>桥模块就绪查询（L1 <see cref="IUiBridgeReadiness"/>；通道未实现 → 返回 None，调用方据显式状态处置）。</summary>
    public async Task<UiBridgeModuleState> GetBridgeModulesAsync(CancellationToken ct = default)
        => _channel is IUiBridgeReadiness r
            ? await r.GetBridgeModulesAsync(ct).ConfigureAwait(false)
            : UiBridgeModuleState.None;

    /// <summary>整页写（显式策略）：有收敛 pump → 收敛写（Committed/Degraded=成功，Failed=失败）；否则纯渲染。</summary>
    private async Task<HtmlWriteBackStatus> WriteFullPageAsync(string html, CancellationToken ct)
    {
        if (_writePump is not null)
        {
            var result = await _writePump(html, ct).ConfigureAwait(false);
            return result.Status;
        }
        await _channel.RenderHtmlAsync(html, ct).ConfigureAwait(false);
        return HtmlWriteBackStatus.Committed;
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

