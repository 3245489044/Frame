#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FrontEndDriven.HtmlViewerBridge;
using Plate.HtmlViewer;
using Plate.JsonMethod.Serialization;
using Plate.JsonMethod.Utilities;
using TConfig;
using UiBackend.Html;

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
        string pageName)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(uiConfig);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(host);

        // P4 域隔离：物理控件 host: 域 + htmlPage 内部元素 page: 域（同名裸控件分域互不覆盖），output/脚本 target 显式带域。
        var index = HtmlDeclarationParser.BuildDomainControlIndex(uiConfig);
        var invoker = TrConfigFunctionInvoker.ToBridgeInvoker(registry);
        // DIP：HtmlBackendRuntime 的写回端口（HtmlWritePump）由连接层 FrontEndDriven.HtmlViewerBridge 提供——
        // ObservedHtmlChannel 把通道包装为 Observe 收敛写（Converged/Degraded），plate 不再感知 Modules.Observe。
        // 注：observed 不在此 dispose——其生命周期须与 HtmlBackendRuntime 一致（写回经 ToWritePump 持续调用；
        // 与原 plate 内 ObserveSessionManager 随控件 GC 收尾语义一致，仅后台确认任务至多存活 _convergeTimeout）。
        var observed = new ObservedHtmlChannel(channel);
        var runtime = new HtmlBackendRuntime(channel, index, invoker, observed.ToWritePump());
        PopulateStaticReadiness(runtime.Readiness, index, registry);
        // P3 实例标识：记录装配所绑定通道的实例（渲染/订阅同源断言；多实例错配可据此定位）。
        runtime.Readiness.ChannelInstanceId = (channel as HtmlViewer)?.InstanceId.ToString("N") ?? channel.GetType().Name;
        // P2 观测：宿主注入的诊断 sink（category,message → 日志门面）接到运行时统一出口。
        var sink = host.DiagnosticsSink;
        if (sink is not null)
        {
            runtime.Diagnostics += sink;
            // P2 观测补漏：HtmlViewer 侧桥诊断（HtmlChannel_webmessage / HtmlChannel_inject_bridge / HtmlChannel_init_failed）
            // 此前只经 HtmlViewer.BridgeDiagnostics 发出、从未接日志门面 → 桥链断点（注入失败/收到 postMessage）全部静默被吞。
            // 统一接入同一 sink，使"点击无效却无日志"的断链在日志可见（对接 fde_log_messages 的 HtmlChannel_* msgId）。
            if (channel is HtmlViewer hv)
                hv.BridgeDiagnostics += sink;
        }

        host.AttachHtmlUi(runtime);
        host.AttachPageDeclaration(ExtractPage(uiConfig, hostControl, pageName));
        if (uiConfig.Templates is { Count: > 0 } templates)
            AttachTemplatesAndTheme(host, templates);
        return runtime.Readiness;
    }

    /// <summary>填充就绪报告静态可判定项：绑定函数是否在注册库、output 写回目标是否唯一命中索引；缺失记入 Problems。</summary>
    private static void PopulateStaticReadiness(
        HtmlRuntimeReadinessReport report,
        IReadOnlyDictionary<string, ControlConfig> index,
        ITrConfigRegistry registry)
    {
        var missing = new List<string>();
        var unresolvedOutputs = new List<string>();
        foreach (var control in index.Values)
        {
            if (control.Events?.EventsDictionary is not { Count: > 0 } functions)
                continue;
            foreach (var fn in functions.Values.SelectMany(f => f))
            {
                if (string.IsNullOrWhiteSpace(fn)) continue;
                if (!registry.Contains(fn))
                    missing.Add($"{control.Name}.{fn}");
            }
            var output = control.Events.Bindings?.Output;
            if (!string.IsNullOrWhiteSpace(output)
                && !index.ContainsKey(output)
                && !index.ContainsKey(HtmlDeclarationParser.HostKey(output))
                && !index.ContainsKey(HtmlDeclarationParser.PageKey(output)))
                unresolvedOutputs.Add($"{control.Name}→{output}");
        }
        report.FunctionsInRegistry = missing.Count == 0;
        report.OutputTargetUnique = unresolvedOutputs.Count == 0;
        foreach (var m in missing)
            report.AddProblem($"绑定函数未在注册库: {m}");
        foreach (var o in unresolvedOutputs)
            report.AddProblem($"output 目标未命中索引: {o}");
    }

    /// <summary>注入页面模板集与 JSON 主题（只需模板集，不依赖 htmlviewer 实例/注册库；宿主可在创建控件前调用，使 JSON 主题在生产生效）。</summary>
    public static void AttachTemplatesAndTheme(HtmlUiHostSlot host, IReadOnlyDictionary<string, string> templates)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(templates);
        host.AttachTemplates(templates);
        // IoC：主题 CSS 外置进 JSON templates.theme，注入全局主题提供者（HtmlViewer 经 HtmlTheme.Resolve 消费）。
        // P4 单一源：从裸 CSS 编译为结构化 ThemeDocument（componentCss 承载原 CSS；后续 JSON 结构化后再拆 token），
        // 经 ConfigThemeProvider + HtmlTheme.AttachProvider 装配（优先于旧 ConfigHtmlTheme 的 IHtmlTheme 路径）。
        if (templates.TryGetValue("theme", out var themeCss) && !string.IsNullOrWhiteSpace(themeCss))
        {
            // P1 主题「基础 + 差异」：框架基础主题（DefaultCss）恒为底，应用 theme 追加/覆盖其上——
            // 消除「框架 DefaultCss 更新而 JSON theme 逐字副本落后 → 静默漂移」的缺口；
            // 应用侧 theme 只需声明差异（业务类），无需复制框架基础部分。
            var baseCss = HtmlAssetRegistry.GetStyle(HtmlAssetKeys.DefaultCss);
            var mergedCss = string.IsNullOrWhiteSpace(baseCss) ? themeCss : baseCss + "\n" + themeCss;
            HtmlTheme.AttachProvider(new ConfigThemeProvider(componentCss: mergedCss));
        }

        // 文档壳模板（P4）：JSON templates.htmlShell 注入全局壳槽（缺省回退内置 DOCTYPE 壳）；
        // 仅「完整文档壳」（含 <html>+<body>）生效，片段壳自动忽略（HtmlDocumentRenderer.AttachShell 内校验）。
        if (templates.TryGetValue("htmlShell", out var shell) && !string.IsNullOrWhiteSpace(shell))
            HtmlDocumentRenderer.AttachShell(shell);
    }

    /// <summary>
    /// 从配置文件加载并注入页面模板/主题（生产接线点：宿主装配插件 UI 后调用，修复「JSON 主题死配置 / HtmlTheme.Resolve 落回内嵌默认」缺口）。
    /// 模板注入与 UI 结构整体解析解耦：仅用 <see cref="JsonDocument"/> 读取 templates 段，UI 结构（form/views/htmlPage）字段类型失配
    /// 不连带丢失模板（修复 8.24 主对话框渲染为空中「整份反序列化抛错 → 模板槽位 null」根因）。
    /// </summary>
    public static async Task<bool> AttachTemplatesAndThemeFromConfigAsync(HtmlUiHostSlot host, string configPath)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(configPath) || !System.IO.File.Exists(configPath))
            return false;
        var templates = await LoadTemplatesAsync(configPath).ConfigureAwait(false);
        if (templates is not { Count: > 0 })
            return false;
        AttachTemplatesAndTheme(host, templates);
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
