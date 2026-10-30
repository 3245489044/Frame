#nullable enable

using System.Text.Json;
using TConfig;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// HTML 事件桥——把页面交互（P5 单协议：渲染端统一 data-cmd="event:{eventName}"，点击桥翻译为 postMessage 负载 {event,name,payload}）
/// 翻译为声明绑定执行：查控件 events 声明 → 装配参数（source=control 经桥读值 / literal / eventPayload）→ 调注册函数 →
/// 结果写回 output 元素。复用 TConfig.EventBinding/ParameterBinding 模型（与 WinForms 事件绑定同构）。
/// </summary>
public sealed class HtmlEventBridge
{
    /// <summary>注册函数调用端口（宿主接线到函数注册表/执行引擎）。参数按形参名绑定；
    /// 返回类型化 <see cref="RenderOutput"/>（叙述文本或写回计划——P0：领域只产产物，落地由协调器负责）。</summary>
    public delegate Task<RenderOutput> HtmlFunctionInvoker(string functionName, IReadOnlyDictionary<string, object?> parameters);

    private readonly IReadOnlyDictionary<string, ControlConfig> _controlsByName;
    private readonly IHtmlChannel _channel;
    private readonly HtmlFunctionInvoker _invoker;

    /// <summary>输出写回目标 → 宿主内容写回端口（output 指向 viewer 宿主自身时经此写回；未注册回落页内 DOM set）。
    /// 端口必须携带来源（<see cref="UiWriteSource"/>）：来源决定收敛窗口/合并优先级，缺省 Mount 会让交互被在飞刷新吞并。</summary>
    private readonly Dictionary<string, Func<string, UiWriteSource, Task>> _hostWriters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>写回计划执行器（写协调器；由运行时 <see cref="HtmlBackendRuntime"/> 注入）。
    /// 计划产物（<see cref="RenderOutput.Plan"/>）经此单点落地——领域侧不再触通道（P0）。</summary>
    public IUiRenderExecutor? RenderExecutor { get; set; }

    /// <summary>交互总线端口（A2）：非空且 <see cref="InteractionRoute"/>=Bus 时，页面事件改走 ECS 总线
    /// （InvocationKernel → FunctionHub → 工作流），写回由总线侧负责。</summary>
    public IHtmlInteractionPort? InteractionPort { get; set; }

    /// <summary>交互路由（A2 灰度开关）：缺省 Legacy（旧直调路径，零回归）。</summary>
    public HtmlInteractionRoute InteractionRoute { get; set; } = HtmlInteractionRoute.Legacy;

    /// <summary>页面受控态仓库（B2/R3）：uiState 回投与 state 参数读取的唯一落点；null = 未装配（回投忽略）。</summary>
    public UiPageStateStore? PageState { get; set; }

    /// <summary>接线框架入口（上层注入；null = 旧直调路径，零回归）。</summary>
    public IUiWireIngress? WireIngress { get; set; }

    /// <summary>Primary = 投递成功后不再直调（唯一投递点）；false = Shadow（并投递 + 直调，仅对拍）。</summary>
    public bool WireIngressPrimary { get; set; }

    public HtmlEventBridge(
        IReadOnlyDictionary<string, ControlConfig> controlsByName,
        IHtmlChannel channel,
        HtmlFunctionInvoker invoker)
    {
        _controlsByName = controlsByName ?? throw new ArgumentNullException(nameof(controlsByName));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    private readonly DiagnosticPort _diagnostics = new();

    /// <summary>桥链路结构化诊断出口（category, message 两元；宿主接线到日志门面，未接线为空操作）。
    /// 埋点分段：EventBridge_route（负载路由）/ EventBridge_invoke（函数调用）/ EventBridge_writeback（写回目标）。</summary>
    public Action<string, string>? Diagnostics { get => _diagnostics.Sink; set => _diagnostics.Sink = value; }

    private void Report(string category, string message) => _diagnostics.Report(category, message);

    /// <summary>处理页面事件负载；返回函数执行结果拼接（写回 output 元素的副作用已执行）。</summary>
    public async Task<string> HandleAsync(string payload, CancellationToken ct = default)
    {
        var envelope = UiMessageEnvelope.Parse(payload);
        ApplyUiState(envelope.UiStateJson);
        var eventName = envelope.EventName ?? string.Empty;
        var controlName = envelope.ControlName;
        var eventPayload = envelope.PayloadText;
        Report("EventBridge_route", $"负载 event={eventName}, name={controlName}");
        if (WireIngress is not null)
        {
            var wireId = "wi_page_" + WireKey(controlName);
            var fields = BuildEventFields(envelope, eventPayload);
            var delivery = await WireIngress.DeliverAsync(wireId, "PageEvent", fields, envelope.Kind, ct).ConfigureAwait(false);
            Report("EventBridge_wire", $"{wireId} delivered={delivery.Delivered} reason={delivery.Reason ?? "-"}");
            if (WireIngressPrimary && delivery.Delivered) return string.Empty;
        }
        if (WireIngressPrimary) return string.Empty; // Primary：接线是唯一投递点，禁 fall-through 二次派发

        // 域隔离解析：data-event 优先 page: 域（页内元素），其次 host: 域（物理控件），回退裸名（兼容裸名索引/测试桩）。
        // 域隔离解析（单一入口）：裸名 → page: → host:（B1 收敛）。
        if (!HtmlDeclarationParser.TryResolveControl(_controlsByName, controlName, out var control))
            return $"未找到控件: {controlName}";

        if (InteractionRoute == HtmlInteractionRoute.Bus && InteractionPort is not null)
            return await DispatchToBusAsync(controlName, eventName, eventPayload, ct).ConfigureAwait(false);

        return (await RunBindingLoopAsync(control, eventName, eventPayload, UiWriteSource.Interactive, ct).ConfigureAwait(false)).Text ?? string.Empty;
    }

    /// <summary>
    /// A2 总线路由：把页面事件投递到交互端口（ECS 事件总线），不再直调注册函数。
    /// 写回由总线侧（RenderOutput 计划 / output 回写）单点落地，本桥返回空文本（禁二次写）。
    /// </summary>
    /// <summary>页面事件直调（不经接线入口）：供接线 handler 复用，避免递归。</summary>
    public async Task<string> DispatchPageEventDirectAsync(
        string? controlName, string eventName, string? eventPayload, CancellationToken ct = default)
    {
        if (!HtmlDeclarationParser.TryResolveControl(_controlsByName, controlName, out var control))
            return $"未找到控件: {controlName}";

        if (InteractionRoute == HtmlInteractionRoute.Bus && InteractionPort is not null)
            return await DispatchToBusAsync(controlName, eventName, eventPayload, ct).ConfigureAwait(false);

        return (await RunBindingLoopAsync(control, eventName, eventPayload, UiWriteSource.Interactive, ct).ConfigureAwait(false)).Text ?? string.Empty;
    }

    private async Task<string> DispatchToBusAsync(
        string? controlName, string eventName, string? eventPayload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(controlName))
        {
            Report("EventBridge_invoke", "总线路由缺少元素名（name），未分发");
            return "事件缺少元素名: name";
        }
        if (string.IsNullOrWhiteSpace(eventName))
        {
            Report("EventBridge_invoke", $"总线路由缺少事件名（element={controlName}），未分发");
            return $"事件缺少事件名: {controlName}";
        }

        var key = ResolveInteractionKey(controlName);
        Report("EventBridge_invoke", $"总线分发 {key}.{eventName}");
        var outcome = await InteractionPort!
            .DispatchAsync(key, eventName, eventPayload, timeoutMs: null, ct)
            .ConfigureAwait(false);

        if (!outcome.Dispatched)
            return string.IsNullOrWhiteSpace(outcome.Error)
                ? $"事件未分发: {key}.{eventName}"
                : outcome.Error!;

        return string.Empty;
    }

    /// <summary>把裸元素名解析为域限定键（page 优先、host 次之、无域声明回退裸名）。</summary>
    private string ResolveInteractionKey(string name)
    {
        if (name.StartsWith(TConfig.InteractionDomains.Host, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(TConfig.InteractionDomains.Page, StringComparison.OrdinalIgnoreCase))
            return name;

        if (_controlsByName.ContainsKey(HtmlDeclarationParser.PageKey(name)))
            return HtmlDeclarationParser.PageKey(name);
        if (_controlsByName.ContainsKey(HtmlDeclarationParser.HostKey(name)))
            return HtmlDeclarationParser.HostKey(name);
        return name;
    }

    /// <summary>装配绑定参数：sourceType=control 经桥读 DOM 值；literal 用字面值；eventPayload 用事件负载原文。</summary>
    private async Task<Dictionary<string, object?>> ResolveParametersAsync(
        EventBinding? bindings, string? eventPayload, CancellationToken ct)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (bindings?.Parameters is not { Count: > 0 })
            return dict;

        foreach (var p in bindings.Parameters)
        {
            ct.ThrowIfCancellationRequested();
            var target = p.Target ?? p.Name;
            if (string.IsNullOrWhiteSpace(target)) continue;

            object? value = null;
            switch (p.SourceType?.ToLowerInvariant())
            {
                case "control" when !string.IsNullOrWhiteSpace(p.Source):
                    var raw = await _channel.ExecuteAsync(DomOps.Get, p.Source, null).ConfigureAwait(false);
                    value = HtmlElementNode.UnwrapString(raw);
                    break;
                // 注：SourceType 已 ToLowerInvariant，case 须全小写（原 "eventPayload" 大小写不匹配 → 误落 default）
                // R3：声明式受控态参数（source 缺省回落声明默认值）。
                case "state":
                    value = string.IsNullOrWhiteSpace(p.Source) ? null : PageState?.GetWithDefault(p.Source!);
                    if (value is null) value = p.DefaultValue;
                    break;
                case "eventpayload":
                    value = eventPayload;
                    break;
                default: // literal（含未声明）
                    value = p.Source;
                    break;
            }
            dict[target] = value;
        }
        return dict;
    }

    /// <summary>解析 postMessage 负载 JSON → (eventName, controlName, eventPayload)。非法输入回退空事件名。
    /// <para>唯一解析实现 = <see cref="UiMessageEnvelope.Parse"/>（本方法只做形态映射，不再次解析）。</para></summary>
    public static (string EventName, string? ControlName, string? Payload) ParsePayload(string payload)
    {
        var envelope = UiMessageEnvelope.Parse(payload);
        return (envelope.EventName ?? string.Empty, envelope.ControlName, envelope.PayloadText);
    }

    /// <summary>
    /// 应用页面回投受控态（R3）：先状态、后事件（同帧顺序固定）。
    /// 未声明槽 → 差集报告（不落状态，禁隐式状态）；open 交回既有展开态通道（本桥只做可见化）。
    /// </summary>
    private void ApplyUiState(string? uiStateJson)
    {
        if (PageState is null || string.IsNullOrWhiteSpace(uiStateJson)) return;
        var patch = UiStatePatch.Parse(uiStateJson);
        if (patch is null)
        {
            Report("EventBridge_uiState", "uiState 解析失败（非对象/非法 JSON）→ 不落状态");
            return;
        }
        if (patch.IsEmpty) return;

        var result = PageState.ApplyPatch(patch);
        var rejected = result.RejectedSlots.Count == 0 ? "-" : string.Join(",", result.RejectedSlots);
        Report("EventBridge_uiState", $"applied scalars={patch.Scalars.Count}, select={patch.Select.Count}, open={result.Open.Count}, rejected=[{rejected}]");
    }

    /// <summary>注册宿主内容写回端口（output 指向宿主 viewer 时经此写回；幂等覆盖）。写入来源由调用方显式给出。</summary>
    public void RegisterHostWriter(string outputName, Func<string, UiWriteSource, Task> writer)
        => _hostWriters[outputName] = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <summary>
    /// 处理宿主 htmlviewer 的 message 事件（MessageReceived 负载原文）→ 按声明 events["message"] 路由：
    /// 查控件声明 → 装配参数（sourceType=eventPayload 取负载原文）→ 逐函数调用 → output 双通道写回。
    /// 宿主由 <paramref name="hostControlName"/> 定位（与 data-event 用负载 name 定位页内控件相区别）。
    /// <paramref name="source"/> = 本次消息触发的写回来源（页面消息缺省 <see cref="UiWriteSource.Interactive"/>）。
    /// </summary>
    public async Task<string> HandleHostMessageAsync(
        string hostControlName, string messagePayload, CancellationToken ct = default,
        UiWriteSource source = UiWriteSource.Interactive)
    {
        var hostEnvelope = UiMessageEnvelope.Parse(messagePayload);
        ApplyUiState(hostEnvelope.UiStateJson);
        if (WireIngress is not null)
        {
            var wireId = "wi_host_event_" + WireKey(hostControlName);
            var fields = BuildHostFields(hostControlName, hostEnvelope, messagePayload);
            var delivery = await WireIngress.DeliverAsync(wireId, "HostMessage", fields, hostEnvelope.Kind, ct).ConfigureAwait(false);
            Report("EventBridge_wire", $"{wireId} delivered={delivery.Delivered} reason={delivery.Reason ?? "-"}");
            if (WireIngressPrimary && delivery.Delivered) return string.Empty;
        }
        if (WireIngressPrimary) return string.Empty; // Primary：接线是唯一投递点，禁 fall-through 二次派发

        Report("EventBridge_route", $"宿主消息 控件={hostControlName} source={source}");
        // 域隔离解析（单一入口）：宿主控件 = host 域偏好（裸名 → host:，不含 page）。
        if (!HtmlDeclarationParser.TryResolveControl(_controlsByName, hostControlName,
                HtmlDeclarationParser.ControlDomain.Host, out var control))
            return $"未找到控件: {hostControlName}";

        return (await RunBindingLoopAsync(control, "message", messagePayload, source, ct).ConfigureAwait(false)).Text ?? string.Empty;
    }

    /// <summary>共用的声明事件绑定执行循环：查 events → 逐函数装配参数 → 调用 → output 写回。HandleAsync / HandleHostMessageAsync 复用。</summary>
    /// <summary>宿主消息直调（不经接线入口）：供接线 handler 复用，避免递归。</summary>
    public async Task<string> DispatchHostMessageDirectAsync(
        string hostControlName, string messagePayload, CancellationToken ct = default,
        UiWriteSource source = UiWriteSource.Interactive)
    {
        if (!HtmlDeclarationParser.TryResolveControl(_controlsByName, hostControlName,
                HtmlDeclarationParser.ControlDomain.Host, out var control))
            return $"未找到控件: {hostControlName}";

        return (await RunBindingLoopAsync(control, "message", messagePayload, source, ct).ConfigureAwait(false)).Text ?? string.Empty;
    }

    private async Task<RenderOutput> RunBindingLoopAsync(
        ControlConfig control, string eventName, string? eventPayload, UiWriteSource source, CancellationToken ct)
    {
        if (control.Events is null
            || !TryGetFunctions(control.Events, eventName, out var functions)
            || functions is not { Count: > 0 })
            return RenderOutput.Narrative($"未声明事件绑定: {control.Name}.{eventName}");

        // 绑定参数与函数名无关：只解析一次（消除逐函数重复解析 + 重复 DOM get 往返）。
        var parameters = await ResolveParametersAsync(control.Events.Bindings, eventPayload, ct).ConfigureAwait(false);
        var texts = new List<string>();
        var plans = new List<UiWritePlan>();
        var planEmitted = false;
        foreach (var functionName in functions)
        {
            ct.ThrowIfCancellationRequested();
            Report("EventBridge_invoke", $"函数 {functionName}, 参数键=[{string.Join(",", parameters.Keys)}]");
            var output = await _invoker(functionName, parameters).ConfigureAwait(false);
            // 每条产物的写回计划已由 WriteOutputAsync 单点落地；此处文本恒保留（不再因首个产物带 plan 而丢文本）。
            await WriteOutputAsync(control.Events.Bindings, output, planEmitted, source, ct).ConfigureAwait(false);
            if (output.HasPlan)
            {
                plans.Add(output.Plan!);
                planEmitted = true;
            }
            if (!string.IsNullOrEmpty(output.Text)) texts.Add(output.Text!);
        }
        if (plans.Count > 1)
            Report("EventBridge_invoke", $"多函数产出 {plans.Count} 个写回计划（均已落地），返回值仅携首个（{plans[0].Kind}）");
        var joined = texts.Count > 0 ? string.Join(Environment.NewLine, texts) : null;
        return new RenderOutput(joined, plans.Count > 0 ? plans[0] : null);
    }

    /// <summary>大小写不敏感地从 EventsDictionary 取指定事件名对应的函数列表。</summary>
    private static bool TryGetFunctions(EventConfig events, string eventName, out List<string>? functions)
    {
        foreach (var kv in events.EventsDictionary)
        {
            if (string.Equals(kv.Key, eventName, StringComparison.OrdinalIgnoreCase))
            {
                functions = kv.Value;
                return true;
            }
        }
        functions = null;
        return false;
    }

    /// <summary>输出写回（P0 类型化）：携带写回计划 → 协调器单点执行（领域侧不触通道）；否则叙述文本经宿主/DOM 写回。</summary>
    /// <summary>wire 坐标键（控件名 → 稳定段：仅保留字母数字，其余转下划线）。</summary>
    private static string WireKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        var chars = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            chars[i] = char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_';
        }

        return new string(chars);
    }

    /// <summary>页面事件 → wire 载荷（协议键平铺；内层 payload JSON 只解析一次）。</summary>
    private static Dictionary<string, string> BuildEventFields(UiMessageEnvelope envelope, string? eventPayload)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = envelope.ControlName ?? string.Empty,
            ["event"] = envelope.EventName ?? string.Empty,
            ["payload"] = eventPayload ?? string.Empty,
            ["uiState"] = envelope.UiStateJson ?? string.Empty,
            ["cmd"] = string.Empty,
        };

        ExtractFields(eventPayload, fields);
        return fields;
    }

    /// <summary>宿主消息 → wire 载荷（cmd 取信封 Kind；内层字段一次解析）。</summary>
    private static Dictionary<string, string> BuildHostFields(
        string hostControl, UiMessageEnvelope envelope, string raw)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control"] = hostControl,
            ["cmd"] = envelope.Kind,
            ["scope"] = envelope.Scope ?? string.Empty,
            ["payload"] = raw,
            ["uiState"] = envelope.UiStateJson ?? string.Empty,
        };

        ExtractFields(raw, fields);
        return fields;
    }

    /// <summary>从 JSON 文本提取协议字段（cmd/session/task/history/type/value/round；已有值不覆盖）。</summary>
    private static void ExtractFields(string? json, Dictionary<string, string> target)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            foreach (var key in new[] { "cmd", "session", "task", "history", "type", "value", "round" })
            {
                if (!root.TryGetProperty(key, out var value)) continue;
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
                if (!string.IsNullOrEmpty(text) && !target.ContainsKey(key)) target[key] = text!;
            }
        }
        catch (JsonException)
        {
            // 非 JSON 负载：保持原文，不在此处报错（格式问题由事件桥原有诊断承担）。
        }
    }

    private async Task WriteOutputAsync(EventBinding? bindings, RenderOutput output, bool planEmitted, UiWriteSource source, CancellationToken ct)
    {
        // P0：计划产物自描述（含目标），由写协调器单点落地——不依赖 binding.Output 名。
        if (output.HasPlan)
        {
            var plan = output.Plan!;
            Report("EventBridge_writeback", $"计划产物 {plan.Kind} → 协调器执行（targets={plan.Targets.Count}）");
            if (RenderExecutor is not null)
                await RenderExecutor.ExecuteAsync(plan, plan.Source, plan.TraceId, ct).ConfigureAwait(false);
            else
                Report("EventBridge_writeback", $"计划产物 {plan.Kind} → 跳过（未装配协调器）");
            return;
        }

        // 目标裁决单点（PageWriteRouting）：与交互总线共用同一判据，本桥只负责落地方式（hostWriter / 声明控件 DOM set）。
        var disposition = PageWriteRouting.Resolve(bindings?.Output, regions: null, hasSelfDescribingPlan: false, pageRuntimeAvailable: false);
        if (disposition.IsUnlandable || disposition.IsRejected)
        {
            Report("EventBridge_writeback", $"目标 {bindings?.Output ?? "(空)"} → 跳过（{disposition.Reason}）");
            return;
        }

        var target = disposition.Target;
        var result = output.Text;
        // L2 类型化：空产物 = 无内容写回（取代 NoWrite 哨兵串；调用方已产计划即返回空文本）。
        if (string.IsNullOrEmpty(result))
        {
            // 同一绑定循环已产出写回计划 ⇒ 空文本是预期伴随产物，不再打“跳过（空产物）”噪声。
            if (!planEmitted)
                Report("EventBridge_writeback", $"目标 {target} → 跳过（空产物 = 无写回内容）");
            return;
        }
        if (_hostWriters.TryGetValue(target, out var writer))
        {
            Report("EventBridge_writeback", $"目标 {target} → hostWriter source={source}");
            await writer(result, source).ConfigureAwait(false);
            return;
        }
        // D6：页内 DOM set 仅限**已声明控件**（白名单）——output 名漂移（未声明）不落地，避免错误文本写坏页面。
        if (!HtmlDeclarationParser.TryResolveControl(_controlsByName, target, out _))
        {
            Report("EventBridge_writeback", $"目标 {target} → 跳过（未声明控件，DOM set 不兜底）");
            return;
        }
        Report("EventBridge_writeback", $"目标 {target} → DOM set");
        await _channel.ExecuteAsync(DomOps.Set, target, result).ConfigureAwait(false);
    }
}
