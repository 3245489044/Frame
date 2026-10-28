#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Abstractions;
using UiBackend.Html;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 桥函数注册库 —— 声明式 HTML 渲染（<c>{prefix}HtmlRender</c>：UIConfiguration → 布局 HTML）、
/// 事件桥（<c>{prefix}HtmlEventDispatch</c>：data-cmd="event:*" 交互 → 绑定函数 → 写回，P5 单协议）、
/// 脚本桥（<c>{prefix}HtmlScript</c>，P3）、装配（<c>{prefix}HtmlAttach</c>）。
/// 机制方法体经注入端口 <see cref="IHtmlUiHostAccess"/> 读取运行时/模板/诊断槽，不反引任何插件静态宿主；
/// 函数名前缀 / 页面名 / 控件名 / 诊断文案均为构造参数（插件差异只在这四个参数）。
/// </summary>
public sealed class HtmlBridgeLibrary
{
    private readonly IHtmlUiHostAccess _backend;
    private readonly HtmlUiHostSlot _host;
    private readonly string _prefix;
    private readonly string _hostControl;
    private readonly string _pageName;
    private readonly HtmlHostingLabels _labels;
    private readonly Func<ITrConfigRegistry?>? _registryProvider;
    private ITrConfigRegistry? _registry;

    /// <summary>默认页面壳模板（未装配 htmlShell 模板时兜底）。css/body 为受信已投影片段，用 |raw 原文绑定。</summary>
    private const string DefaultHtmlShell = "<style>{css|raw}</style>{body|raw}";

    /// <summary>空模板集（未装配兜底；防止 null 传播）。</summary>
    public static readonly IReadOnlyDictionary<string, string> EmptyTemplates = new Dictionary<string, string>();

    /// <param name="access">宿主装配态只读端口（实时读取）。</param>
    /// <param name="host">目标宿主槽实例（装配写回点）。</param>
    /// <param name="prefix">桥函数名前缀（如 <c>"AgentUi_"</c> / <c>"ProduceUi_"</c>）。</param>
    /// <param name="hostControl">承载页面内部声明的宿主控件名。</param>
    /// <param name="pageName">页面内部声明名（htmlPage.name）。</param>
    /// <param name="labels">诊断文案标签（缺省通用中性文案）。</param>
    /// <param name="registryProvider">函数注册库提供者（装配时解析事件绑定端口用；<see cref="Register"/> 已登记库时优先用登记值）。</param>
    public HtmlBridgeLibrary(
        IHtmlUiHostAccess access,
        HtmlUiHostSlot host,
        string prefix,
        string hostControl,
        string pageName,
        HtmlHostingLabels? labels = null,
        Func<ITrConfigRegistry?>? registryProvider = null)
    {
        _backend = access ?? throw new ArgumentNullException(nameof(access));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("前缀不可为空", nameof(prefix));
        _prefix = prefix;
        _hostControl = hostControl ?? throw new ArgumentNullException(nameof(hostControl));
        _pageName = pageName ?? throw new ArgumentNullException(nameof(pageName));
        _labels = labels ?? new HtmlHostingLabels();
        _registryProvider = registryProvider;
    }

    /// <summary>未装配 HTML 后端时的诊断文案。</summary>
    public string HtmlUiNotAttached => _labels.NotAttached;

    /// <summary>注册 4 个通用桥函数（前缀参数化）到注册库；同时登记该库供后续装配复用。</summary>
    public void Register(ITrConfigRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;

        registry.Register(_prefix + "HtmlRender",
            (Func<string, Task<string>>)(HtmlRenderAsync),
            new IParamConfig[] { Param("configJson", typeof(string)) },
            "渲染 UIConfiguration JSON → HTML body（含主题 style；布局经 HTML 后端投影，供 htmlviewer 显示）");

        registry.Register(_prefix + "HtmlEventDispatch",
            (Func<string, Task<string>>)(HtmlEventDispatchAsync),
            new IParamConfig[] { Param("payload", typeof(string)) },
            "处理 HTML 事件桥负载（data-event 交互 → 查声明绑定 → 装配参数 → 调函数 → 结果写回 output 元素）");

        registry.Register(_prefix + "HtmlScript",
            (Func<string, Task<string>>)(HtmlScriptAsync),
            new IParamConfig[] { Param("scriptJson", typeof(string)) },
            "执行 HTML 脚本（04-json-script 命令协议 set/get/click/exec/sleep → HTML 原生元素；{template} 仅 echo-section）");

        registry.Register(_prefix + "HtmlDomScript",
            (Func<string, string, Task<string>>)(HtmlDomScriptAsync),
            new IParamConfig[]
            {
                Param("scriptJson", typeof(string)),
                Param("hostControl", typeof(string), optional: true, def: null),
            },
            "对指定 htmlviewer 执行 DOM 脚本（set/get/click/realclick/sleep；不装配/不接管消息桥）。参数：scriptJson=命令 JSON；hostControl=目标控件名（缺省用库宿主控件）");

        registry.Register(_prefix + "HtmlAttach",
            (Func<Task<string>>)(HtmlAttachAsync),
            "装配 HTML 后端运行时（DOM 桥 + 页面声明 + 模板 + 主题）；页面 sideload 后调用");
    }

    /// <summary>渲染 UIConfiguration JSON → HTML body（含主题 style）；供 htmlviewer 直接显示。</summary>
    public Task<string> HtmlRenderAsync(string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return Task.FromResult("（configJson 为空）");
        try
        {
            var config = HtmlDeclarationParser.Parse(configJson);
            if (config is null) return Task.FromResult("（configJson 解析失败）");
            var result = HtmlUiProjector.Project(config);
            // 页面壳：优先装配的 htmlShell 模板，否则内嵌默认壳。css/body 为受信已投影片段，一律 |raw 原文绑定，
            // 统一经 HtmlTemplateEngine 渲染（消除手写字符串拼接 <style>…</style> 兜底路径）。
            var templates = _backend.Templates ?? EmptyTemplates;
            var shell = templates.TryGetValue("htmlShell", out var configured) ? configured : DefaultHtmlShell;
            var html = HtmlTemplateEngine.RenderTemplate(shell, null,
                new Dictionary<string, object?>
                {
                    ["css"] = result.Css,
                    ["body"] = result.BodyHtml,
                });
            return Task.FromResult(html);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"HTML 渲染失败[{ex.GetType().Name}]: {ex.Message}");
        }
    }

    /// <summary>处理页面 data-event 交互负载 → 声明绑定函数执行 + 结果写回 output 元素。</summary>
    public async Task<string> HtmlEventDispatchAsync(string payload)
    {
        // 交互入口自愈（B）：控件实例可能因配置重载/作用域重建被更换——先按「装配身份」校验并重挂，
        // 否则事件产出无法落地（旧运行时把写回/计划执行器留在旧实例上）。
        await EnsureHtmlUiAttachedAsync().ConfigureAwait(false);
        var runtime = _backend.HtmlUi;
        if (runtime is null) return HtmlUiNotAttached;
        try
        {
            return await runtime.HandleEventAsync(payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"HTML 事件分发失败[{ex.GetType().Name}]: {ex.Message}";
        }
    }

    /// <summary>执行 HTML 脚本（04-json-script 命令协议 → HTML 原生元素）。命令解析（含 {template} 展开）统一经通用
    /// <see cref="HtmlScriptBridge.ParseCommands"/>（单一真源，不在插件侧私有实现）。</summary>
    public async Task<string> HtmlScriptAsync(string scriptJson)
    {
        // 交互入口自愈（B），语义同 HtmlEventDispatchAsync。
        await EnsureHtmlUiAttachedAsync().ConfigureAwait(false);
        var runtime = _backend.HtmlUi;
        if (runtime is null) return HtmlUiNotAttached;
        try
        {
            var commands = HtmlScriptBridge.ParseCommands(scriptJson);
            var result = await runtime.ScriptBridge.ExecuteAsync(commands, CancellationToken.None).ConfigureAwait(false);
            // 判定失败不可退化为成功文本：命令级分型（NOT_EFFECTIVE/OCCLUDED/UNSUPPORTED/...）⇒ 抛型异常，
            // 由 InvocationKernel 归因 FunctionFault，exec 命令随即判红（不再依赖脚本文案子串）。
            if (result.HasFailure) throw new HtmlScriptFailedException(result);
            return result.ToText();
        }
        catch (HtmlScriptFailedException)
        {
            // 判据失败必须到达调用方；若被下方通用 catch 吞成成功文本，等于把判据彻底废掉。
            throw;
        }
        catch (Exception ex)
        {
            return $"HTML 脚本执行失败[{ex.GetType().Name}]: {ex.Message}";
        }
    }

    /// <summary>
    /// 对指定 htmlviewer 执行 DOM 脚本（set/get/click/realclick/sleep），**不装配 HTML 运行时、不接管消息桥**。
    /// <para>用途：v2 单页控制台（如 ProducePanel 的 tabConsoleViewer）仍由声明式 events.message 承载消息桥时，
    /// 测试/自动化需要真实 DOM 操作但又不能抢占消息订阅；此入口直接经 <see cref="HtmlViewer.ExecuteDomAsync"/>
    /// 对目标控件执行白名单 DOM 指令。与 <see cref="HtmlScriptAsync"/> 的区别仅是不依赖宿主槽装配态。</para>
    /// </summary>
    /// <param name="scriptJson">04-json-script 命令 JSON（整脚本或裸命令数组）。</param>
    /// <param name="hostControl">目标 htmlviewer 控件名；空 = 库宿主控件。</param>
    public async Task<string> HtmlDomScriptAsync(string scriptJson, string? hostControl = null)
    {
        var control = string.IsNullOrWhiteSpace(hostControl) ? _hostControl : hostControl!;
        var viewer = _backend.HtmlViewerResolver?.Invoke(control);
        if (viewer is null)
            return $"未找到宿主控件 {control}（未 sideload 或 resolver 未注入）";

        var commands = HtmlScriptBridge.ParseCommands(scriptJson);
        if (commands.Count == 0)
            return "（脚本为空或解析失败）";

        var lines = new List<string>();
        foreach (var cmd in commands)
        {
            var action = cmd.Action?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (action)
            {
                case DomOps.Set:
                {
                    var status = await viewer.ExecuteDomAsync(DomOps.Set, cmd.Target ?? string.Empty, cmd.Value)
                        .ConfigureAwait(false);
                    var text = HtmlElementNode.UnwrapString(status);
                    if (!DomOps.IsOk(text))
                        throw new InvalidOperationException($"[set] {cmd.Target} → {text}");
                    lines.Add($"[set] {cmd.Target} = {cmd.Value}");
                    break;
                }

                case DomOps.Get:
                {
                    var raw = await viewer.ExecuteDomAsync(DomOps.Get, cmd.Target ?? string.Empty, null)
                        .ConfigureAwait(false);
                    lines.Add($"[get] {cmd.Target} = {HtmlElementNode.UnwrapString(raw)}");
                    break;
                }

                case DomOps.Click:
                case DomOps.RealClick:
                {
                    var raw = await viewer.ExecuteDomAsync(action, cmd.Target ?? string.Empty, null)
                        .ConfigureAwait(false);
                    var text = HtmlElementNode.UnwrapString(raw);
                    lines.Add($"[{action}] {cmd.Target} → {text}");
                    var failed = action == DomOps.RealClick
                        ? ClickOutcomes.IsFailure(ClickOutcomes.Parse(text))
                        : !DomOps.IsOk(text);
                    if (failed)
                        throw new InvalidOperationException(string.Join(Environment.NewLine, lines));
                    break;
                }

                case "sleep":
                {
                    var ms = int.TryParse(cmd.Target ?? cmd.Value, out var parsed) && parsed > 0 ? parsed : 200;
                    await Task.Delay(ms).ConfigureAwait(false);
                    lines.Add($"[sleep] {ms}ms");
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"[{cmd.Action}] 不支持（DOM 桥白名单：set/get/click/realclick/sleep）");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// A2 交互端口：从组合根解析 ECS 事件端口并包装为 Plate 本地端口；未装配 → null（回退旧直调）。
    /// </summary>
    private static IHtmlInteractionPort? ResolveInteractionPort()
    {
        var port = AppServiceProvider.Resolve<IInteractionEventPort>();
        return port is null ? null : new HtmlInteractionAdapter(port);
    }

    /// <summary>
    /// A2 交互路由灰度：环境变量 <c>WF_HTML_INTERACTION_ROUTE=legacy|bus</c> 显式覆盖；
    /// 未显式声明时「端口可得 ⇒ bus」——使声明式链路默认生效，回退只需设 legacy。
    /// </summary>
    private static HtmlInteractionRoute ResolveInteractionRoute()
    {
        var configured = Environment.GetEnvironmentVariable("WF_HTML_INTERACTION_ROUTE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim().Equals("legacy", StringComparison.OrdinalIgnoreCase)
                ? HtmlInteractionRoute.Legacy
                : HtmlInteractionRoute.Bus;
        }

        return ResolveInteractionPort() is null ? HtmlInteractionRoute.Legacy : HtmlInteractionRoute.Bus;
    }

    /// <summary>
    /// 装配 HTML 后端运行时（DOM 桥 + 页面声明 + 模板 + 主题 + 消息桥订阅 + 回流探针）：页面 sideload 后调用。
    /// 取控件经 <see cref="IHtmlUiHostAccess.HtmlViewerResolver"/>，注册库取 <see cref="Register"/> 登记值（回退注入的提供者），
    /// 配置路径取 <see cref="IHtmlUiHostAccess.ConfigPath"/> —— 消除「完整装配生产无调用点」缺口。
    /// </summary>
    public async Task<string> HtmlAttachAsync()
    {
        // B1 幂等装配（**身份化**）：仅当「已装配 ∧ 运行时绑定的通道仍是当前解析到的控件实例」才跳过重复装配。
        // 只看 HasHtmlUi 会把「控件已被配置重载/作用域重建换掉」误判为「已装配」——旧运行时把
        // HostContentWriter / RenderPlanExecutor 写在旧实例上，新实例无执行器 ⇒ ECS 计划产物无法落地（面板空）。
        if (IsAttachedToCurrentViewer())
            return $"（HTML 后端已装配且绑定当前控件，幂等跳过重复装配；hostControl={_hostControl}）";
        DetachStaleRuntime();
        try
        {
            var viewer = _backend.HtmlViewerResolver?.Invoke(_hostControl);
            if (viewer is null)
                return ReportAttachFailure($"{_labels.ViewerUnresolved}（hostControl={_hostControl}）");
            var registry = _registryProvider?.Invoke() ?? _registry;
            if (registry is null)
                return ReportAttachFailure(_labels.RegistryNotAttached);
            var configPath = _backend.ConfigPath;
            if (string.IsNullOrWhiteSpace(configPath) || !System.IO.File.Exists(configPath))
                return ReportAttachFailure(_labels.ConfigPathNotReady(configPath));
            var uiConfig = HtmlBridgeAssembly.Load(configPath);
            if (uiConfig is null)
                return ReportAttachFailure($"（配置加载失败）configPath={configPath}");
            var report = HtmlBridgeAssembly.Attach(viewer, uiConfig, registry, _host, _hostControl, _pageName,
                ResolveInteractionPort(), ResolveInteractionRoute());
            var runtime = _backend.HtmlUi;

            // 声明驱动：按宿主控件声明 events.message + bindings 自动绑定 MessageReceived。
            // 函数名 / 参数映射(sourceType=eventPayload) / output 写回(双通道: 宿主→SetHtmlAsync) 全部来自配置声明，
            // 不在 C# 硬编码函数名 / 手动订阅（消除「手动点击 data-cmd 无效」断链与声明架空）。
            if (runtime is not null)
            {
                var attachError = runtime.AttachHostMessage(viewer, _hostControl, _backend.HtmlContentSink);
                if (attachError is not null)
                    report.AddProblem(attachError);
            }

            // P0 回流探针：真实 WebView2 下确认「DOM postMessage → C# 消息桥入口」闭环（不依赖可选 sink）。
            // 自愈重试（最多 3 次，短暂间隔）：装配时序/导航竞态会让首次探针在 WebView2 初始化/导航未稳时瞬时失败，
            // 而失败若被当作「非必需」忽略，正是「桥就绪=True 但消息桥从未生效 → 点击静默无效」的掩盖根因。
            // 故此处 ① 重试自愈；② 仍失败即计入 Problems → 就绪契约强制「装配不完整」（不再返回「已装配」掩盖回流断裂）。
            if (runtime is not null && report.MessageSubscribed && report.HostWriterRegistered)
            {
                const int ProbeAttempts = 3;
                report.TransportLoopbackOk = false;
                for (var attempt = 1; attempt <= ProbeAttempts && !report.TransportLoopbackOk; attempt++)
                {
                    report.TransportLoopbackOk = await runtime.Channel.LoopbackProbeAsync(CancellationToken.None).ConfigureAwait(false);
                    if (!report.TransportLoopbackOk && attempt < ProbeAttempts)
                        await Task.Delay(200).ConfigureAwait(false);
                }
                _backend.DiagnosticsSink?.Invoke("HtmlChannel_loopback",
                    report.TransportLoopbackOk
                        ? "回流链路自检通过（postMessage → C# 消息桥入口闭环）"
                        : $"回流链路自检失败（postMessage 未达 C# 消息桥；疑似未订阅/实例错配/传输断裂；重试 {ProbeAttempts} 次均失败）");
                if (!report.TransportLoopbackOk)
                    report.AddProblem("回流链路自检失败（DOM postMessage 未达 C# 消息桥入口；回流断裂，交互将静默无效）");
            }

            // P1 就绪契约强制：缺任一绑定/问题即返回明确「装配不完整」失败，不再以「已装配」掩盖（消除「桥就绪=True 但消息桥从未生效」）。
            if (!report.IsFullyReady)
            {
                var problems = report.Problems.Count > 0
                    ? string.Join("; ", report.Problems)
                    : report.Summary();
                return $"HTML 装配不完整（就绪契约未满足）: {problems}";
            }

            return _labels.AttachedSummary(
                report.Summary(),
                _backend.Templates is not null,
                _backend.PageDeclaration is not null);
        }
        catch (Exception ex)
        {
            return $"HTML 装配失败[{ex.GetType().Name}]: {ex.Message}";
        }
    }

    /// <summary>
    /// 自愈装配：未装配 HTML 后端时执行一次完整装配（DOM 桥 + 消息桥订阅 + 回流探针）；已装配直接返回。
    /// 生产宿主 OnLoad 中 resolver/控件晚于 sideload 就绪，此处由「页面首次渲染 / 消息桥消费前」触发，
    /// 保证消息桥在任何交互前已订阅（消除生产无调用点缺口）。
    /// <para>返回装配结论文本（成功摘要或失败原因）：失败不得静默——否则「宿主控件名写错」会表现为
    /// 点击无效/整页重建而无任何日志证据（NoSilentOutlet）。</para>
    /// </summary>
    public async Task<string> EnsureHtmlUiAttachedAsync()
    {
        // 身份化判据（A）：控件实例被配置重载/作用域重建换掉后，旧运行时的执行器留在旧实例 ⇒ 必须重挂。
        if (IsAttachedToCurrentViewer()) return $"（HTML 后端已装配且绑定当前控件，hostControl={_hostControl}）";
        return await HtmlAttachAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// **附加宿主控件接线**（数据库页 R2）：把额外 htmlviewer 控件登记到**同一** runtime 的消息桥
    /// （hostWriter + <c>events.message</c> 订阅），**不重建 runtime**。
    /// 返回 <c>null</c>=成功；非空字符串=失败原因（可见化，不抛）。
    /// </summary>
    /// <param name="extraHostControl">附加宿主控件名（须为声明内 htmlviewer 控件且声明 events.message）。</param>
    public string? AttachExtraHost(string extraHostControl)
    {
        if (string.IsNullOrWhiteSpace(extraHostControl)) return "extraHostControl 为空";
        if (!IsAttachedToCurrentViewer())
            return $"HTML 后端未绑定当前控件（{_hostControl}）⇒ 须先重新装配（{_prefix}HtmlAttach）";
        var runtime = _backend.HtmlUi;
        if (runtime is null) return HtmlUiNotAttached;
        var viewer = _backend.HtmlViewerResolver?.Invoke(extraHostControl);
        if (viewer is null) return $"未找到宿主控件 {extraHostControl}（未 sideload 或 resolver 未注入）";
        return runtime.AttachHostMessage(viewer, extraHostControl, _backend.HtmlContentSink);
    }

    /// <summary>
    /// 装配身份判据（结构事实）：已装配 ∧ 运行时绑定的通道 == 当前解析到的控件实例。
    /// 载体 = 实例引用（<see cref="HtmlBackendRuntime.Channel"/> ↔ <see cref="IHtmlUiHostAccess.HtmlViewerResolver"/>），
    /// 不是「HasHtmlUi 布尔」——后者无法区分「同一控件」与「控件已重建」。
    /// </summary>
    private bool IsAttachedToCurrentViewer()
    {
        if (!_backend.HasHtmlUi) return false;
        var runtime = _backend.HtmlUi;
        if (runtime is null) return false;
        var current = _backend.HtmlViewerResolver?.Invoke(_hostControl);
        return current is not null && ReferenceEquals(runtime.Channel, current);
    }

    /// <summary>
    /// 解装配陈旧运行时（控件实例已变更）：先清槽位、再释放旧订阅，防孤儿 handler 与新运行时双触发；
    /// 随后由调用方重新 Attach（新实例重新获得 HostContentWriter / RenderPlanExecutor）。
    /// </summary>
    private void DetachStaleRuntime()
    {
        var previous = _backend.HtmlUi;
        if (previous is null) return;
        _host.DetachHtmlUi();
        previous.Dispose();
        _backend.DiagnosticsSink?.Invoke("HtmlChannel_attach",
            "HTML 后端绑定控件实例已变更（配置重载/作用域重建）→ 解装配旧运行时并重新装配（消除『已装配但执行器在旧实例』的落地断链）。");
    }

    /// <summary>装配失败统一出口：诊断可见（宿主控件名/注册库/配置路径错配不再静默）。</summary>
    private string ReportAttachFailure(string message)
    {
        _backend.DiagnosticsSink?.Invoke("HtmlChannel_attach_failed", message);
        return message;
    }

    /// <summary>函数参数描述符（注册库注册用）。</summary>
    private static IParamConfig Param(string name, Type type, bool optional = false, object? def = null)
        => new ParameterDescriptor
        {
            Name = name,
            Type = type,
            IsOptional = optional,
            DefaultValue = def
        };
}
