#nullable enable

using System;
using System.Collections.Generic;
using Plate.HtmlViewer;
using Share.HostAssembly.Concurrency;
using TConfig;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 宿主槽（**实例承载**）—— 承载某一插件宿主注入的 HTML/渲染相关槽位
/// （htmlviewer 解析器 / 内容写回 sink / 诊断 sink / 配置路径 / 模板 / 页面声明 / DOM 桥运行时）。
/// 每插件一个实例（经 <see cref="HtmlUiHostRegistry"/> 按 key 取用），避免多插件共用单一全局槽互相覆盖；
/// 线程安全：全部槽位共享本实例的 <see cref="_gate"/>。
/// 装配单一入口 <see cref="ComposeHtmlHost"/>；缺绑定经 <see cref="GetBridgeAssemblySnapshot"/> 可查询断言。
/// </summary>
public sealed class HtmlUiHostSlot
{
    private readonly object _gate = new();

    private readonly ThreadSafeSlot<HtmlBackendRuntime> _htmlUi;
    private readonly ThreadSafeSlot<IReadOnlyDictionary<string, string>> _templates;
    private readonly ThreadSafeSlot<UIConfiguration> _pageDeclaration;
    private readonly ThreadSafeSlot<Func<string, HtmlViewer?>> _htmlViewerResolver;
    private readonly ThreadSafeSlot<Action<string>> _htmlContentSink;
    private readonly ThreadSafeSlot<Action<string, string>> _diagnosticsSink;
    private string? _configPath;

    public HtmlUiHostSlot()
    {
        _htmlUi = new ThreadSafeSlot<HtmlBackendRuntime>(_gate);
        _templates = new ThreadSafeSlot<IReadOnlyDictionary<string, string>>(_gate);
        _pageDeclaration = new ThreadSafeSlot<UIConfiguration>(_gate);
        _htmlViewerResolver = new ThreadSafeSlot<Func<string, HtmlViewer?>>(_gate);
        _htmlContentSink = new ThreadSafeSlot<Action<string>>(_gate);
        _diagnosticsSink = new ThreadSafeSlot<Action<string, string>>(_gate);
    }

    /// <summary>当前装配的 HTML 后端运行时；未装配为 null。</summary>
    public HtmlBackendRuntime? HtmlUi => _htmlUi.Value;

    /// <summary>是否已装配 HTML 后端运行时。</summary>
    public bool HasHtmlUi => _htmlUi.Has;

    /// <summary>装配 HTML 后端运行时（幂等：覆盖式）。传入需非 null。</summary>
    public void AttachHtmlUi(HtmlBackendRuntime runtime) => _htmlUi.Attach(runtime);

    /// <summary>解除 HTML 后端运行时槽位。</summary>
    public void DetachHtmlUi() => _htmlUi.Detach();

    /// <summary>当前装配的模板集；未装配为 null（渲染器回退内嵌默认）。</summary>
    public IReadOnlyDictionary<string, string>? Templates => _templates.Value;

    /// <summary>是否已装配模板集。</summary>
    public bool HasTemplates => _templates.Has;

    /// <summary>装配模板集（幂等：覆盖式）。传入需非 null。</summary>
    public void AttachTemplates(IReadOnlyDictionary<string, string> templates) => _templates.Attach(templates);

    /// <summary>解除模板集槽位。</summary>
    public void DetachTemplates() => _templates.Detach();

    /// <summary>当前装配的页面内部声明；未装配为 null。</summary>
    public UIConfiguration? PageDeclaration => _pageDeclaration.Value;

    /// <summary>是否已装配页面内部声明。</summary>
    public bool HasPageDeclaration => _pageDeclaration.Has;

    /// <summary>装配页面内部声明（幂等：覆盖式）。传入需非 null。</summary>
    public void AttachPageDeclaration(UIConfiguration declaration) => _pageDeclaration.Attach(declaration);

    /// <summary>解除页面内部声明槽位。</summary>
    public void DetachPageDeclaration() => _pageDeclaration.Detach();

    /// <summary>宿主注入的 htmlviewer 控件解析器；未注入为 null。</summary>
    public Func<string, HtmlViewer?>? HtmlViewerResolver => _htmlViewerResolver.Value;

    /// <summary>是否已注入 htmlviewer 控件解析器。</summary>
    public bool HasHtmlViewerResolver => _htmlViewerResolver.Has;

    /// <summary>由宿主注入 htmlviewer 控件解析器（幂等：覆盖式）。传入需非 null。</summary>
    public void AttachHtmlViewerResolver(Func<string, HtmlViewer?> resolver) => _htmlViewerResolver.Attach(resolver);

    /// <summary>解除 htmlviewer 控件解析器槽位。</summary>
    public void DetachHtmlViewerResolver() => _htmlViewerResolver.Detach();

    /// <summary>宿主注入的页面内容同步回调；未注入为 null。</summary>
    public Action<string>? HtmlContentSink => _htmlContentSink.Value;

    /// <summary>是否已注入页面内容同步回调。</summary>
    public bool HasHtmlContentSink => _htmlContentSink.Has;

    /// <summary>由宿主注入页面内容同步回调（幂等：覆盖式）。传入需非 null。</summary>
    public void AttachHtmlContentSink(Action<string> sink) => _htmlContentSink.Attach(sink);

    /// <summary>解除页面内容同步回调槽位。</summary>
    public void DetachHtmlContentSink() => _htmlContentSink.Detach();

    /// <summary>桥链路诊断 sink；未注入为 null。</summary>
    public Action<string, string>? DiagnosticsSink => _diagnosticsSink.Value;

    /// <summary>是否已注入桥链路诊断 sink。</summary>
    public bool HasDiagnosticsSink => _diagnosticsSink.Has;

    /// <summary>由宿主注入桥链路诊断 sink（幂等：覆盖式）。传入需非 null。</summary>
    public void AttachDiagnosticsSink(Action<string, string> sink) => _diagnosticsSink.Attach(sink);

    /// <summary>解除桥链路诊断 sink 槽位。</summary>
    public void DetachDiagnosticsSink() => _diagnosticsSink.Detach();

    /// <summary>宿主解析出的插件 UI 配置路径；未回填为 null。</summary>
    public string? ConfigPath
    {
        get { lock (_gate) return _configPath; }
    }

    /// <summary>由宿主回填配置路径（幂等：覆盖式）。</summary>
    public void AttachConfigPath(string? configPath)
    {
        lock (_gate) _configPath = configPath;
    }

    /// <summary>解除配置路径槽位。</summary>
    public void DetachConfigPath()
    {
        lock (_gate) _configPath = null;
    }

    /// <summary>
    /// 组合根单一装配入口：一次装配全部 HTML/渲染相关槽位（组合根快照，替代散落 AttachXxx 的人肉接线）。
    /// 未提供的槽位保持原值（不覆盖）。装配后经 <see cref="GetBridgeAssemblySnapshot"/> 查询缺绑定。
    /// </summary>
    public void AttachComposition(HtmlUiHostComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        lock (_gate)
        {
            if (composition.HtmlUi is not null) _htmlUi.Attach(composition.HtmlUi);
            if (composition.Templates is not null) _templates.Attach(composition.Templates);
            if (composition.PageDeclaration is not null) _pageDeclaration.Attach(composition.PageDeclaration);
            if (composition.HtmlViewerResolver is not null) _htmlViewerResolver.Attach(composition.HtmlViewerResolver);
            if (composition.HtmlContentSink is not null) _htmlContentSink.Attach(composition.HtmlContentSink);
            if (composition.DiagnosticsSink is not null) _diagnosticsSink.Attach(composition.DiagnosticsSink);
            if (composition.ConfigPath is not null) _configPath = composition.ConfigPath;
        }
    }

    /// <summary>
    /// HTML 宿主装配单一入口：一次收敛 resolver / contentSink / diagnosticsSink / configPath 四个散落槽位，
    /// 消除「宿主记得调每个 AttachXxx」的人肉接线；返回可查询装配快照供就绪断言（缺绑定即 visible）。幂等：重复调用覆盖式装配。
    /// 模板/主题注入（<c>HtmlBridgeAssembly.AttachTemplatesAndThemeFromConfigAsync</c>）由宿主单独调用（含异常防护，不并入本入口）。
    /// </summary>
    public HtmlBridgeAssemblySnapshot ComposeHtmlHost(
        Func<string, HtmlViewer?>? viewerResolver,
        Action<string>? contentSink,
        Action<string, string>? diagnosticsSink,
        string? configPath)
    {
        AttachComposition(new HtmlUiHostComposition(
            HtmlViewerResolver: viewerResolver,
            HtmlContentSink: contentSink,
            DiagnosticsSink: diagnosticsSink,
            ConfigPath: configPath));
        return GetBridgeAssemblySnapshot();
    }

    /// <summary>查询当前 HTML 装配清单（持锁取各槽位状态；供宿主装配校验/诊断，消除能力孤岛）。</summary>
    public HtmlBridgeAssemblySnapshot GetBridgeAssemblySnapshot()
    {
        lock (_gate)
            return new HtmlBridgeAssemblySnapshot(
                _htmlUi.Has,
                _templates.Has,
                _pageDeclaration.Has,
                _htmlViewerResolver.Has,
                _htmlContentSink.Has,
                _diagnosticsSink.Has,
                !string.IsNullOrWhiteSpace(_configPath));
    }
}
