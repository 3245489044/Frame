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
/// 机制方法体经注入端口 <see cref="IHtmlBackendAttachment"/> 读取运行时/模板/诊断槽，不反引任何插件静态宿主；
/// 函数名前缀 / 页面名 / 控件名 / 诊断文案均为构造参数（插件差异只在这四个参数）。
/// </summary>
public sealed class HtmlBridgeLibrary
{
    private readonly IHtmlBackendAttachment _backend;
    private readonly HtmlUiHostSlot _host;
    private readonly string _prefix;
    private readonly string _hostControl;
    private readonly string _pageName;
    private readonly HtmlHostingLabels _labels;
    private readonly Func<ITrConfigRegistry?>? _registryProvider;
    private ITrConfigRegistry? _registry;

    /// <summary>装配生命周期协调器（身份化幂等 / 解装配 / 探针 / 失败去重；唯一实现）。</summary>
    private readonly HtmlAttachmentCoordinator _attach;

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
        IHtmlBackendAttachment access,
        HtmlUiHostSlot host,
        string prefix,
        string hostControl,
        string pageName,
        HtmlHostingLabels? labels = null,
        Func<ITrConfigRegistry?>? registryProvider = null)
        : this(access, host, prefix, hostControl, pageName, labels, registryProvider, null)
    {
    }

    /// <param name="access">宿主装配态只读端口（实时读取）。</param>
    /// <param name="host">目标宿主槽实例（装配写回点）。</param>
    /// <param name="prefix">桥函数名前缀（如 <c>"AgentUi_"</c> / <c>"ProduceUi_"</c>）。</param>
    /// <param name="hostControl">承载页面内部声明的宿主控件名。</param>
    /// <param name="pageName">页面内部声明名（htmlPage.name）。</param>
    /// <param name="labels">诊断文案标签（缺省通用中性文案）。</param>
    /// <param name="registryProvider">函数注册库提供者（装配时解析事件绑定端口用；<see cref="Register"/> 已登记库时优先用登记值）。</param>
    /// <param name="interactionPolicy">交互端口/路由策略（缺省组合根 + 环境变量；测试可注入替身）。</param>
    public HtmlBridgeLibrary(
        IHtmlBackendAttachment access,
        HtmlUiHostSlot host,
        string prefix,
        string hostControl,
        string pageName,
        HtmlHostingLabels? labels,
        Func<ITrConfigRegistry?>? registryProvider,
        HtmlInteractionRoutePolicy? interactionPolicy)
    {
        _backend = access ?? throw new ArgumentNullException(nameof(access));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("前缀不可为空", nameof(prefix));
        _prefix = prefix;
        _hostControl = hostControl ?? throw new ArgumentNullException(nameof(hostControl));
        _pageName = pageName ?? throw new ArgumentNullException(nameof(pageName));
        _labels = labels ?? new HtmlHostingLabels();
        _registryProvider = registryProvider;
        _attach = new HtmlAttachmentCoordinator(
            _backend, _host, _hostControl, _pageName, _labels,
            () => _registryProvider?.Invoke() ?? _registry,
            interactionPolicy);
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

        // 白名单命令执行唯一实现 = Plate 的 HtmlDomCommandRunner（含空脚本判定与失败分型）。
        return await HtmlDomCommandRunner.ExecuteAsync(viewer, scriptJson).ConfigureAwait(false);
    }

    /// <summary>
    /// 装配 HTML 后端运行时（DOM 桥 + 页面声明 + 模板 + 主题 + 消息桥订阅 + 回流探针）：页面 sideload 后调用。
    /// 过程唯一实现 = <see cref="HtmlAttachmentCoordinator"/>（本类只做桥函数适配）。
    /// </summary>
    public Task<string> HtmlAttachAsync() => _attach.AttachAsync();

    /// <summary>
    /// 自愈装配：未装配 HTML 后端时执行一次完整装配（DOM 桥 + 消息桥订阅 + 回流探针）；已装配直接返回。
    /// 生产宿主 OnLoad 中 resolver/控件晚于 sideload 就绪，此处由「页面首次渲染 / 消息桥消费前」触发，
    /// 保证消息桥在任何交互前已订阅（消除生产无调用点缺口）。
    /// <para>返回装配结论文本（成功摘要或失败原因）：失败不得静默——否则「宿主控件名写错」会表现为
    /// 点击无效/整页重建而无任何日志证据（NoSilentOutlet）。</para>
    /// </summary>
    public Task<string> EnsureHtmlUiAttachedAsync() => _attach.EnsureAttachedAsync();

    /// <summary>
    /// **附加宿主控件接线**（数据库页 R2）：把额外 htmlviewer 控件登记到**同一** runtime 的消息桥
    /// （hostWriter + <c>events.message</c> 订阅），**不重建 runtime**。
    /// 返回 <c>null</c>=成功；非空字符串=失败原因（可见化，不抛）。
    /// </summary>
    /// <param name="extraHostControl">附加宿主控件名（须为声明内 htmlviewer 控件且声明 events.message）。</param>
    public string? AttachExtraHost(string extraHostControl)
    {
        if (string.IsNullOrWhiteSpace(extraHostControl)) return "extraHostControl 为空";
        if (!_attach.IsAttachedToCurrentViewer())
            return $"HTML 后端未绑定当前控件（{_hostControl}）⇒ 须先重新装配（{_prefix}HtmlAttach）";
        return _attach.AttachExtraHost(extraHostControl);
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
