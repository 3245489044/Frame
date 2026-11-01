#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.HtmlViewerBridge;
using Plate.HtmlViewer;
using Plate.JsonMethod.Serialization;
using Plate.JsonMethod.Utilities;
using Strategy.Refresh.Core;
using Strategy.Refresh.Diagnostics;
using TConfig;
using UiBackend.Abstractions;
using UiBackend.Html;
using Share.EventBindingOrchestrator.Binding;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 后端运行时装配辅助（组合根）——把「宿主注入的 DOM 桥 + 声明源 + 函数注册库」组装为
/// <see cref="HtmlBackendRuntime"/> 并挂到 <see cref="HtmlUiHost"/>（消除能力孤岛）：
/// ① 控件树 → 域索引（<see cref="HtmlDeclarationParser.BuildDomainControlIndex"/>，事件桥/脚本桥用，P4 域隔离）
/// ② 注册库 → 事件桥函数调用端口（<see cref="TrConfigFunctionInvoker.ToBridgeInvoker"/>）
/// ③ 页面内部声明（配置 JSON 的 htmlPage）提取后注入 <see cref="HtmlUiHost.AttachPageDeclaration"/>
/// 页面名 / 控件名由调用方参数化（各插件不同），不再硬编码常量。
/// </summary>
public static class HtmlBridgeAssembly
{
    /// <summary>
    /// 兜底诊断出口（宿主未注入 sink 时使用）：桥/通道诊断此前只在「宿主主动注入 sink」时才可见，
    /// 脚本、自检、无 PrjBridge 的宿主上表现为「点击无效且零日志」。兜底只做分类落盘：
    /// 失败类 → Warning，其余（路由/写回/通道）→ Info。
    /// </summary>
    private static readonly Action<string, string> BridgeDiagnosticsFallback = (category, message) =>
    {
        var isProblem = category.Contains("error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("失败", StringComparison.Ordinal)
            || message.Contains("未找到", StringComparison.Ordinal)
            || message.Contains("未分发", StringComparison.Ordinal)
            || message.Contains("拒绝", StringComparison.Ordinal)
            || message.Contains("不完整", StringComparison.Ordinal);

        if (isProblem)
            FrontEndDrivenLog.Log("HtmlBridge_DiagnosticProblem", new { category, message });
        else
            FrontEndDrivenLog.Log("HtmlBridge_Diagnostic", new { category, message });
    };

    /// <summary>
    /// 装配 HTML 后端运行时并挂载到 <see cref="HtmlUiHost"/>。返回装配就绪报告（P1 就绪契约：函数在注册表 /
    /// output 目标唯一已填充；hostWriter/message 订阅由宿主随后调用 <see cref="HtmlBackendRuntime.AttachHostMessage"/> 更新到同一报告引用）。
    /// </summary>
    /// <param name="channel">统一传递通道（宿主页面 htmlviewer，实现 <see cref="IHtmlChannel"/>）。</param>
    /// <param name="uiConfig">整体 UIConfiguration（配置 JSON 解析结果，含宿主控件 htmlPage）。</param>
    /// <param name="registry">插件函数注册库（ITrConfigRegistry，函数调用端口）。</param>
    /// <param name="host">目标宿主槽实例（写回点；每插件一个实例，互不覆盖）。</param>
    /// <param name="hostControl">承载页面内部声明的宿主控件名。</param>
    /// <param name="pageName">页面内部声明名（htmlPage.name）。</param>
    public static HtmlRuntimeReadinessReport Attach(
        IHtmlChannel channel,
        UIConfiguration uiConfig,
        ITrConfigRegistry registry,
        HtmlUiHostSlot host,
        string hostControl,
        string pageName,
        IHtmlInteractionPort? interactions = null,
        HtmlInteractionRoute interactionRoute = HtmlInteractionRoute.Legacy)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(uiConfig);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(host);

        // P4 域隔离：物理控件 host: 域 + htmlPage 内部元素 page: 域（同名裸控件分域互不覆盖），output/脚本 target 显式带域。
        var index = HtmlDeclarationParser.BuildDomainControlIndex(uiConfig);
        // R2/R3：页面区域目录与受控态声明（区域目标合法性 + uiState 回投落点）。
        var pageDeclaration = HtmlDeclarationParser.FindPageDeclaration(uiConfig, hostControl, pageName);
        var invoker = TrConfigFunctionInvoker.ToBridgeInvoker(registry);
        // A1：声明式工作流也是合法绑定目标——就绪检查认「注册函数 ∪ 已声明工作流」，否则 page 绑定会被误判缺失。
        var workflowNames = new HashSet<string>(
            InteractionGraphProjector.CollectWorkflows(InteractionGraphProjector.Project(uiConfig), out _).Keys,
            StringComparer.OrdinalIgnoreCase);
        // DIP：HtmlBackendRuntime 的写回端口（HtmlWritePump）由连接层 FrontEndDriven.HtmlViewerBridge 提供——
        // ObservedHtmlChannel 把通道包装为 Observe 收敛写（Converged/Degraded），plate 不再感知 Modules.Observe。
        // 注：observed 不在此 dispose——其生命周期须与 HtmlBackendRuntime 一致（写回经 ToWritePump 持续调用；
        // 与原 plate 内 ObserveSessionManager 随控件 GC 收尾语义一致，仅后台确认任务至多存活 _convergeTimeout）。
        var observed = new ObservedHtmlChannel(channel);
        // 写仲裁（P2 决策上移）：每 slot 单调版本 + baseline 过期即丢 + 指纹去重——由通用刷新框架单一状态源
        // （InMemoryRefreshStore）承载；两生产者交替写同 slot 时旧版本判 Stale、不触 DOM（治 region/tree ping-pong）。
        var writeArbiter = new RefreshWriteArbiter(new InMemoryRefreshStore());
        // P3 指纹单源 + P4 追踪单源：写落地与领域共用框架侧实现（plate 只经注入使用，不反向依赖）。
        // B5：帧预算与指标在此接线（参数单源 = Plate 的 HtmlWriteRuntimeOptionsFactory；装配代码不再持有魔法数）。
        var writeOptions = HtmlWriteRuntimeOptionsFactory.CreateDefault();
        var writeMetrics = new UiWriteMetrics();
        var runtime = new HtmlBackendRuntime(channel, index, invoker, observed.ToWritePump(), writeArbiter,
            Fnv1aFingerprint.Default.Of, RefreshTraceId.New, writeOptions, writeMetrics);
        // A2：页面事件可选改走交互总线（缺省 Legacy = 旧直调，零回归）。
        // R2/R3：写入装配面（区域声明 + 状态声明）；未声明 regions ⇒ 区域目标一律拒绝（fail-fast 可见化）。
        runtime.PageRegions = RegionCatalog.From(pageDeclaration);
        if (pageDeclaration is not null)
        {
            // 区域键单源下传：渲染层读同一份 htmlPage.regions（禁 C# 第二份键表）。
            host.AttachPageLayout(pageDeclaration);
        }
        runtime.PageState.Initialize(pageDeclaration?.State);
        runtime.InteractionPort = interactions;
        runtime.InteractionRoute = interactionRoute;
        // F1：消息边界只投递（O(1)），取数/渲染在单 worker 串行；DOM 落地仍经 IHtmlChannel 单点封送。
        runtime.MessageScheduler = new HostMessageCommandScheduler();
        HtmlAttachContract.PopulateStaticReadiness(runtime.Readiness, index, registry, workflowNames);
        HtmlAttachContract.ValidateRegionTargets(runtime.Readiness, index, runtime.PageRegions);
        // P3 实例标识：记录装配所绑定通道的实例（渲染/订阅同源断言；多实例错配可据此定位）。
        runtime.Readiness.ChannelInstanceId = (channel as HtmlViewer)?.InstanceId.ToString("N") ?? channel.GetType().Name;
        // P2 观测：宿主注入的诊断 sink（category,message → 日志门面）接到运行时统一出口。
        var sink = host.DiagnosticsSink;
        if (sink is null)
        {
            // 兜底：宿主未注入 sink（脚本/自检/无 PrjBridge 宿主）时不得让桥诊断静默消失。
            sink = BridgeDiagnosticsFallback;
            host.AttachDiagnosticsSink(sink);
        }

        if (sink is not null)
        {
            runtime.Diagnostics += sink;
            // 开关可见化（单发）：运行期即可判定「预算是否真的开着」，不靠读源码（描述与参数同源）。
            sink("UiWriteOptions", HtmlWriteRuntimeOptionsFactory.Describe(writeOptions));
            // P2 观测补漏：HtmlViewer 侧桥诊断（HtmlChannel_webmessage / HtmlChannel_inject_bridge / HtmlChannel_init_failed）
            // 此前只经 HtmlViewer.BridgeDiagnostics 发出、从未接日志门面 → 桥链断点（注入失败/收到 postMessage）全部静默被吞。
            // 统一接入同一 sink，使"点击无效却无日志"的断链在日志可见（对接 fde_log_messages 的 HtmlChannel_* msgId）。
            if (channel is HtmlViewer hv)
                hv.BridgeDiagnostics += sink;
            // 节/拍遥测（D6）：每节 nodes/bytes/jsMs/outcome + 每拍 batchNodes/lane/queueMs/droppedSections。
            runtime.SectionTelemetry = line => sink("UiWriteSection", line);
        }

        // S4 接线：流面「按结果自愈」需要带结果的执行器（未显式装配时由此兜底，职责仍属单写者）。
        if (channel is HtmlViewer planViewer)
            planViewer.RenderPlanResultExecutor ??= plan => runtime.ExecuteAsync(plan, plan.Source, plan.TraceId);

        host.AttachHtmlUi(runtime);
        // 页面内部声明可为空（区域式页面只声明 regions、无内部控件；由插件自行渲染 HTML）。
        // 空值不得进入槽位（ThreadSafeSlot.Attach 对 null 直接抛 MissingDependencyException，
        // 会把「无内部页面声明」误判为装配失败并中断后续消息桥/计划执行器接线）。
        var pageDeclarationConfig = ExtractPage(uiConfig, hostControl, pageName);
        if (pageDeclarationConfig is not null)
            host.AttachPageDeclaration(pageDeclarationConfig);
        AttachTemplatesAndTheme(host, uiConfig.Templates ?? new Dictionary<string, string>());
        return runtime.Readiness;
    }

    /// <summary>注入页面模板集与 JSON 主题（只需模板集，不依赖 htmlviewer 实例/注册库；宿主可在创建控件前调用，使 JSON 主题在生产生效）。
    /// 通用 HTML 由框架默认资产承担（经 <see cref="IHtmlAssetProvider"/> 端口读取，配置可按同名键覆盖）；
    /// 消费方不再直取具体内核的静态成员，消除跨程序集「成员改名即 MissingField」耦合。</summary>
    /// <summary>兼容重载（二进制稳定）：保留旧 2 参签名，供已编译消费方调用。</summary>
    public static void AttachTemplatesAndTheme(
        HtmlUiHostSlot host,
        IReadOnlyDictionary<string, string> templates)
        => AttachTemplatesAndTheme(host, templates, null);

    public static void AttachTemplatesAndTheme(
        HtmlUiHostSlot host,
        IReadOnlyDictionary<string, string> templates,
        IHtmlAssetProvider? assets)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(templates);

        // 框架默认模板为底，配置显式模板覆盖其上（合成单源 = Plate 的 HtmlAttachContract）。
        var resolved = HtmlAttachContract.ComposeTemplates(
            assets ?? HtmlAssetRegistry.Default, templates, host.DiagnosticsSink);

        host.AttachTemplates(resolved);
        // P0 键契约（I2/I4）：全元素 × 同父键唯一性 + 缺稳定键（<details> 无键 → 重渲染后展开态无法按 key 恢复）。
        // 经诊断 sink 可见化后装配期 fail-fast（键由渲染层派生，不靠作者手写）。
        HtmlAttachContract.EnforceTemplateKeys(resolved, host.DiagnosticsSink);
        // P4 单一源：从裸 CSS 编译为结构化 ThemeDocument（componentCss 承载原 CSS），
        // 经 ConfigThemeProvider + HtmlTheme.AttachProvider 装配（优先于旧 ConfigHtmlTheme 的 IHtmlTheme 路径）。
        if (resolved.TryGetValue("theme", out var themeCss) && !string.IsNullOrWhiteSpace(themeCss))
        {
            // P1 主题「基础 + 差异」：框架基础主题恒为底，应用 theme 追加/覆盖其上（应用只声明差异）。
            HtmlTheme.AttachProvider(new ConfigThemeProvider(
                componentCss: HtmlAttachContract.ComposeThemeCss(HtmlDocumentRenderer.DefaultCss, themeCss)));
        }

        // 文档壳模板（P4）：合成集 htmlShell 注入全局壳槽（缺省回退内置 DOCTYPE 壳）；
        // 仅「完整文档壳」（含 <html>+<body>）生效，片段壳自动忽略（HtmlDocumentRenderer.AttachShell 内校验）。
        if (resolved.TryGetValue("htmlShell", out var shell) && !string.IsNullOrWhiteSpace(shell))
            HtmlDocumentRenderer.AttachShell(shell);
    }

    /// <summary>
    /// 从配置文件加载并注入页面模板/主题（生产接线点：宿主装配插件 UI 后调用，修复「JSON 主题死配置 / HtmlTheme.Resolve 落回内嵌默认」缺口）。
    /// 模板注入与 UI 结构整体解析解耦：仅用 <see cref="JsonDocument"/> 读取 templates 段，UI 结构（form/views/htmlPage）字段类型失配
    /// 不连带丢失模板（修复 8.24 主对话框渲染为空中「整份反序列化抛错 → 模板槽位 null」根因）。
    /// </summary>
    /// <summary>兼容重载（二进制稳定）：保留旧 2 参签名，供已编译消费方（PrjBridge/插件 DLL）调用。</summary>
    public static Task<bool> AttachTemplatesAndThemeFromConfigAsync(
        HtmlUiHostSlot host,
        string configPath)
        => AttachTemplatesAndThemeFromConfigAsync(host, configPath, null);

    public static async Task<bool> AttachTemplatesAndThemeFromConfigAsync(
        HtmlUiHostSlot host,
        string configPath,
        IHtmlAssetProvider? assets)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(configPath) || !System.IO.File.Exists(configPath))
            return false;
        var templates = await LoadTemplatesAsync(configPath).ConfigureAwait(false)
            ?? new Dictionary<string, string>();
        AttachTemplatesAndTheme(host, templates, assets);
        return true;
    }

    /// <summary>仅从配置文件读取 templates 段（经 Plate.JsonMethod 取 JSON 节点 + 读 string-map，不做整份 UI 结构反序列化）。</summary>
    private static async Task<IReadOnlyDictionary<string, string>?> LoadTemplatesAsync(string configPath)
    {
        var node = await JsonSerializationService.Instance
            .LoadJsonNodeFromFileAsync(configPath).ConfigureAwait(false);
        return JsonPathHelper.ReadStringMap(node, "$.templates");
    }

    /// <summary>从配置文件加载整体 <see cref="UIConfiguration"/>（供完整 Attach 复用；仅解析声明，不注入模板）。
    /// 解析经 Plate.HtmlViewer 的 <see cref="HtmlDeclarationParser.Load"/>。</summary>
    public static UIConfiguration? Load(string configPath)
        => HtmlDeclarationParser.Load(configPath);

    /// <summary>从整体声明提取页面内部页面声明（<paramref name="hostControl"/> 的 htmlPage）；无 → null。
    /// 提取组合（定位宿主控件 + 取内部页面）经 Plate.HtmlViewer 通用入口 <see cref="HtmlDeclarationParser.FindHtmlPage"/>，
    /// 调用方只提供控件/页面名参数。</summary>
    public static UIConfiguration? ExtractPage(UIConfiguration uiConfig, string hostControl, string pageName)
        => HtmlDeclarationParser.FindHtmlPage(uiConfig, hostControl, pageName);
}
