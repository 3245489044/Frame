#nullable enable

using System.Text.Json;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// HTML 事件桥——把页面交互（P5 单协议：渲染端统一 data-cmd="event:{eventName}"，点击桥翻译为 postMessage 负载 {event,name,payload}）
/// 翻译为声明绑定执行：查控件 events 声明 → 装配参数（source=control 经桥读值 / literal / eventPayload）→ 调注册函数 →
/// 结果写回 output 元素。复用 TConfig.EventBinding/ParameterBinding 模型（与 WinForms 事件绑定同构）。
/// </summary>
public sealed class HtmlEventBridge
{
    /// <summary>注册函数调用端口（宿主接线到函数注册表/执行引擎）。参数按形参名绑定。</summary>
    public delegate Task<string> HtmlFunctionInvoker(string functionName, IReadOnlyDictionary<string, object?> parameters);

    private readonly IReadOnlyDictionary<string, ControlConfig> _controlsByName;
    private readonly IHtmlChannel _channel;
    private readonly HtmlFunctionInvoker _invoker;

    /// <summary>输出写回目标 → 宿主内容写回端口（output 指向 htmlviewer 宿主自身时经此 SetHtmlAsync；未注册回落页内 DOM set）。</summary>
    private readonly Dictionary<string, Func<string, Task>> _hostWriters = new(StringComparer.OrdinalIgnoreCase);

    public HtmlEventBridge(
        IReadOnlyDictionary<string, ControlConfig> controlsByName,
        IHtmlChannel channel,
        HtmlFunctionInvoker invoker)
    {
        _controlsByName = controlsByName ?? throw new ArgumentNullException(nameof(controlsByName));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    /// <summary>桥链路结构化诊断出口（category, message 两元；宿主接线到日志门面，未接线为空操作）。
    /// 埋点分段：EventBridge_route（负载路由）/ EventBridge_invoke（函数调用）/ EventBridge_writeback（写回目标）。</summary>
    public Action<string, string>? Diagnostics;

    private void Report(string category, string message) => Diagnostics?.Invoke(category, message);

    /// <summary>处理页面事件负载；返回函数执行结果拼接（写回 output 元素的副作用已执行）。</summary>
    public async Task<string> HandleAsync(string payload, CancellationToken ct = default)
    {
        var (eventName, controlName, eventPayload) = ParsePayload(payload);
        Report("EventBridge_route", $"负载 event={eventName}, name={controlName}");
        // 域隔离解析：data-event 优先 page: 域（页内元素），其次 host: 域（物理控件），回退裸名（兼容裸名索引/测试桩）。
        if (string.IsNullOrWhiteSpace(controlName)
            || (!_controlsByName.TryGetValue(controlName, out var control)
                && !_controlsByName.TryGetValue(HtmlDeclarationParser.PageKey(controlName), out control)
                && !_controlsByName.TryGetValue(HtmlDeclarationParser.HostKey(controlName), out control)))
            return $"未找到控件: {controlName}";

        return await RunBindingLoopAsync(control, eventName, eventPayload, ct).ConfigureAwait(false);
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

    /// <summary>解析 postMessage 负载 JSON → (eventName, controlName, eventPayload)。非法输入回退空事件名。</summary>
    public static (string EventName, string? ControlName, string? Payload) ParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (string.Empty, null, null);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var eventName = root.TryGetProperty("event", out var evt) ? evt.GetString() : string.Empty;
            var name = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            var payloadStr = root.TryGetProperty("payload", out var pl) && pl.ValueKind == JsonValueKind.String
                ? pl.GetString()
                : root.GetRawText();
            return (eventName ?? string.Empty, name, payloadStr);
        }
        catch (JsonException)
        {
            return (string.Empty, null, payload);
        }
    }

    /// <summary>注册宿主内容写回端口（output 指向宿主 htmlviewer 时经此写回 SetHtmlAsync；幂等覆盖）。</summary>
    public void RegisterHostWriter(string outputName, Func<string, Task> writer)
        => _hostWriters[outputName] = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <summary>
    /// 处理宿主 htmlviewer 的 message 事件（MessageReceived 负载原文）→ 按声明 events["message"] 路由：
    /// 查控件声明 → 装配参数（sourceType=eventPayload 取负载原文）→ 逐函数调用 → output 双通道写回。
    /// 宿主由 <paramref name="hostControlName"/> 定位（与 data-event 用负载 name 定位页内控件相区别）。
    /// </summary>
    public async Task<string> HandleHostMessageAsync(string hostControlName, string messagePayload, CancellationToken ct = default)
    {
        Report("EventBridge_route", $"宿主消息 控件={hostControlName}");
        // 域隔离解析：宿主控件经 host: 域键定位，回退裸名（兼容裸名索引/测试桩）。
        if (!_controlsByName.TryGetValue(hostControlName, out var control)
            && !_controlsByName.TryGetValue(HtmlDeclarationParser.HostKey(hostControlName), out control))
            return $"未找到控件: {hostControlName}";

        return await RunBindingLoopAsync(control, "message", messagePayload, ct).ConfigureAwait(false);
    }

    /// <summary>共用的声明事件绑定执行循环：查 events → 逐函数装配参数 → 调用 → output 写回。HandleAsync / HandleHostMessageAsync 复用。</summary>
    private async Task<string> RunBindingLoopAsync(
        ControlConfig control, string eventName, string? eventPayload, CancellationToken ct)
    {
        if (control.Events is null
            || !TryGetFunctions(control.Events, eventName, out var functions)
            || functions is not { Count: > 0 })
            return $"未声明事件绑定: {control.Name}.{eventName}";

        var results = new List<string>();
        foreach (var functionName in functions)
        {
            ct.ThrowIfCancellationRequested();
            var parameters = await ResolveParametersAsync(control.Events.Bindings, eventPayload, ct).ConfigureAwait(false);
            Report("EventBridge_invoke", $"函数 {functionName}, 参数键=[{string.Join(",", parameters.Keys)}]");
            var result = await _invoker(functionName, parameters).ConfigureAwait(false);
            await WriteOutputAsync(control.Events.Bindings, result, ct).ConfigureAwait(false);
            results.Add(result);
        }
        return string.Join(Environment.NewLine, results);
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

    /// <summary>输出双通道写回：output 命中已注册宿主写回端口 → SetHtmlAsync；否则 → 页内 DOM set。</summary>
    private async Task WriteOutputAsync(EventBinding? bindings, string result, CancellationToken ct)
    {
        var output = bindings?.Output;
        if (string.IsNullOrWhiteSpace(output))
            return;
        if (_hostWriters.TryGetValue(output, out var writer))
        {
            Report("EventBridge_writeback", $"目标 {output} → hostWriter");
            await writer(result).ConfigureAwait(false);
            return;
        }
        Report("EventBridge_writeback", $"目标 {output} → DOM set");
        await _channel.ExecuteAsync(DomOps.Set, output, result).ConfigureAwait(false);
    }
}
