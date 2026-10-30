#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// HTML 宿主装配协调器（通用件，唯一实现）—— 把「身份化幂等判据 / 陈旧运行时解装配 / 完整装配过程 /
/// 回流探针自愈 / 失败可见化去重 / 附加宿主接线」从桥函数库中分离：桥函数库、宿主启动流程、自动化入口
/// 复用同一份装配时序，不再各自复制（复制即两套装配语义，控件实例更换后必现「执行器留在旧实例」）。
/// </summary>
public sealed class HtmlAttachmentCoordinator
{
    private readonly IHtmlBackendAttachment _backend;
    private readonly HtmlUiHostSlot _host;
    private readonly string _hostControl;
    private readonly string _pageName;
    private readonly HtmlHostingLabels _labels;
    private readonly Func<ITrConfigRegistry?> _registryProvider;
    private readonly HtmlInteractionRoutePolicy _interactionPolicy;

    /// <summary>最近一次装配失败签名（同因重复失败不重复发诊断，避免逐轮轮询刷屏）。</summary>
    private string? _lastAttachFailure;

    /// <param name="backend">宿主装配态只读端口（实时读取）。</param>
    /// <param name="host">目标宿主槽实例（装配写回点）。</param>
    /// <param name="hostControl">承载页面内部声明的宿主控件名。</param>
    /// <param name="pageName">页面内部声明名（htmlPage.name）。</param>
    /// <param name="labels">诊断文案标签（缺省通用中性文案）。</param>
    /// <param name="registryProvider">函数注册库提供者（装配期解析；null ⇒ 视为未装配）。</param>
    /// <param name="interactionPolicy">交互端口/路由策略（缺省组合根 + 环境变量策略）。</param>
    public HtmlAttachmentCoordinator(
        IHtmlBackendAttachment backend,
        HtmlUiHostSlot host,
        string hostControl,
        string pageName,
        HtmlHostingLabels? labels = null,
        Func<ITrConfigRegistry?>? registryProvider = null,
        HtmlInteractionRoutePolicy? interactionPolicy = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _hostControl = hostControl ?? throw new ArgumentNullException(nameof(hostControl));
        _pageName = pageName ?? throw new ArgumentNullException(nameof(pageName));
        _labels = labels ?? new HtmlHostingLabels();
        _registryProvider = registryProvider ?? (static () => null);
        _interactionPolicy = interactionPolicy ?? HtmlInteractionRoutePolicy.Default;
    }

    /// <summary>最近一次装配失败签名（诊断/测试可读）。</summary>
    public string? LastAttachFailure => _lastAttachFailure;

    /// <summary>
    /// 装配身份判据（结构事实）：已装配 ∧ 运行时绑定的通道 == 当前解析到的控件实例。
    /// 载体 = 实例引用（<see cref="HtmlBackendRuntime.Channel"/> ↔ <see cref="IHtmlBackendAttachment.HtmlViewerResolver"/>），
    /// 不是「HasHtmlUi 布尔」——后者无法区分「同一控件」与「控件已重建」。
    /// </summary>
    public bool IsAttachedToCurrentViewer()
    {
        if (!_backend.HasHtmlUi) return false;
        var runtime = _backend.HtmlUi;
        if (runtime is null) return false;
        var current = _backend.HtmlViewerResolver?.Invoke(_hostControl);
        if (current is null || !ReferenceEquals(runtime.Channel, current)) return false;
        // 装配完整性：runtime 已挂但消息桥/计划执行器未挂到当前控件 = 部分装配（装配中途失败），
        // 必须视为「未装配」触发重挂，否则会被幂等判据永久跳过（表现为交互写回计划无法落地）。
        return current.RenderPlanExecutor is not null;
    }

    /// <summary>
    /// 解装配陈旧运行时（控件实例已变更）：先清槽位、再释放旧订阅，防孤儿 handler 与新运行时双触发；
    /// 随后由调用方重新 Attach（新实例重新获得 HostContentWriter / RenderPlanExecutor）。
    /// </summary>
    public void DetachStaleRuntime()
    {
        var previous = _backend.HtmlUi;
        if (previous is null) return;
        _host.DetachHtmlUi();
        previous.Dispose();
        _backend.DiagnosticsSink?.Invoke("HtmlChannel_attach",
            "HTML 后端绑定控件实例已变更（配置重载/作用域重建）→ 解装配旧运行时并重新装配（消除『已装配但执行器在旧实例』的落地断链）。");
    }

    /// <summary>装配失败统一出口：诊断可见（宿主控件名/注册库/配置路径错配不再静默）。
    /// <para>同因失败只发一次（如面板尚未 sideload 时的逐轮轮询）：重复同签名失败静默，避免刷屏；
    /// 原因变化或装配成功后重置。</para></summary>
    public string ReportFailure(string message)
    {
        if (!string.Equals(_lastAttachFailure, message, StringComparison.Ordinal))
        {
            _lastAttachFailure = message;
            _backend.DiagnosticsSink?.Invoke("HtmlChannel_attach_failed", message);
        }
        return message;
    }

    /// <summary>
    /// 装配 HTML 后端运行时（DOM 桥 + 页面声明 + 模板 + 主题 + 消息桥订阅 + 回流探针）：页面 sideload 后调用。
    /// 取控件经 <see cref="IHtmlBackendAttachment.HtmlViewerResolver"/>，注册库取提供者，配置路径取
    /// <see cref="IHtmlBackendAttachment.ConfigPath"/> —— 消除「完整装配生产无调用点」缺口。
    /// </summary>
    public async Task<string> AttachAsync()
    {
        // B1 幂等装配（**身份化**）：仅当「已装配 ∧ 运行时绑定的通道仍是当前解析到的控件实例」才跳过重复装配。
        // 只看 HasHtmlUi 会把「控件已被配置重载/作用域重建换掉」误判为「已装配」——旧运行时把
        // HostContentWriter / RenderPlanExecutor 写在旧实例上，新实例无执行器 ⇒ ECS 计划产物无法落地（面板空）。
        if (IsAttachedToCurrentViewer())
            return $"（HTML 后端已装配且绑定当前控件，幂等跳过重复装配；hostControl={_hostControl}）";
        DetachStaleRuntime();
        try
        {
            // 两种情况语义完全不同：解析器未注入 = 真装配缺失；解析器返回 null = 控件尚未创建（时序状态）。
            var resolver = _backend.HtmlViewerResolver;
            if (resolver is null)
                return ReportFailure(_labels.ViewerUnresolved);
            var viewer = resolver(_hostControl);
            if (viewer is null)
                return ReportFailure(_labels.HostControlNotFound(_hostControl));
            var registry = _registryProvider();
            if (registry is null)
                return ReportFailure(_labels.RegistryNotAttached);
            var configPath = _backend.ConfigPath;
            if (string.IsNullOrWhiteSpace(configPath) || !System.IO.File.Exists(configPath))
                return ReportFailure(_labels.ConfigPathNotReady(configPath));
            var uiConfig = HtmlBridgeAssembly.Load(configPath);
            if (uiConfig is null)
                return ReportFailure($"（配置加载失败）configPath={configPath}");
            var report = HtmlBridgeAssembly.Attach(viewer, uiConfig, registry, _host, _hostControl, _pageName,
                _interactionPolicy.ResolvePort(), _interactionPolicy.ResolveRoute());
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

            _lastAttachFailure = null;
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
    public async Task<string> EnsureAttachedAsync()
    {
        // 身份化判据（A）：控件实例被配置重载/作用域重建换掉后，旧运行时的执行器留在旧实例 ⇒ 必须重挂。
        if (IsAttachedToCurrentViewer())
        {
            _lastAttachFailure = null;
            return $"（HTML 后端已装配且绑定当前控件，hostControl={_hostControl}）";
        }

        return await AttachAsync().ConfigureAwait(false);
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
            return $"HTML 后端未绑定当前控件（{_hostControl}）⇒ 须先重新装配";
        var runtime = _backend.HtmlUi;
        if (runtime is null) return _labels.NotAttached;
        var viewer = _backend.HtmlViewerResolver?.Invoke(extraHostControl);
        if (viewer is null) return $"未找到宿主控件 {extraHostControl}（未 sideload 或 resolver 未注入）";
        return runtime.AttachHostMessage(viewer, extraHostControl, _backend.HtmlContentSink);
    }

}
