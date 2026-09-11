#nullable enable

using System;
using System.Collections.Generic;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 宿主门面（**默认实例的静态门面**）—— 转发到 <see cref="Default"/>（<see cref="HtmlUiHostSlot"/> 实例）。
/// 保留静态形态以兼容既有消费方（Agent 组合根 / 测试）；需多插件隔离时改用
/// <see cref="HtmlUiHostRegistry.For"/> 取各自实例，避免共用单一全局槽互相覆盖。
/// </summary>
public static class HtmlUiHost
{
    /// <summary>默认宿主槽实例（进程级单例；单一 HTML 宿主插件的既有接线点）。</summary>
    public static HtmlUiHostSlot Default { get; } = new();

    /// <summary>当前装配的 HTML 后端运行时；未装配为 null。</summary>
    public static HtmlBackendRuntime? HtmlUi => Default.HtmlUi;

    /// <summary>是否已装配 HTML 后端运行时。</summary>
    public static bool HasHtmlUi => Default.HasHtmlUi;

    /// <summary>装配 HTML 后端运行时（幂等：覆盖式）。传入需非 null。</summary>
    public static void AttachHtmlUi(HtmlBackendRuntime runtime) => Default.AttachHtmlUi(runtime);

    /// <summary>解除 HTML 后端运行时槽位。</summary>
    public static void DetachHtmlUi() => Default.DetachHtmlUi();

    /// <summary>当前装配的模板集；未装配为 null（渲染器回退内嵌默认）。</summary>
    public static IReadOnlyDictionary<string, string>? Templates => Default.Templates;

    /// <summary>是否已装配模板集。</summary>
    public static bool HasTemplates => Default.HasTemplates;

    /// <summary>装配模板集（幂等：覆盖式）。传入需非 null。</summary>
    public static void AttachTemplates(IReadOnlyDictionary<string, string> templates) => Default.AttachTemplates(templates);

    /// <summary>解除模板集槽位。</summary>
    public static void DetachTemplates() => Default.DetachTemplates();

    /// <summary>当前装配的页面内部声明；未装配为 null。</summary>
    public static UIConfiguration? PageDeclaration => Default.PageDeclaration;

    /// <summary>是否已装配页面内部声明。</summary>
    public static bool HasPageDeclaration => Default.HasPageDeclaration;

    /// <summary>装配页面内部声明（幂等：覆盖式）。传入需非 null。</summary>
    public static void AttachPageDeclaration(UIConfiguration declaration) => Default.AttachPageDeclaration(declaration);

    /// <summary>解除页面内部声明槽位。</summary>
    public static void DetachPageDeclaration() => Default.DetachPageDeclaration();

    /// <summary>宿主注入的 htmlviewer 控件解析器；未注入为 null。</summary>
    public static Func<string, HtmlViewer?>? HtmlViewerResolver => Default.HtmlViewerResolver;

    /// <summary>是否已注入 htmlviewer 控件解析器。</summary>
    public static bool HasHtmlViewerResolver => Default.HasHtmlViewerResolver;

    /// <summary>由宿主注入 htmlviewer 控件解析器（幂等：覆盖式）。传入需非 null。</summary>
    public static void AttachHtmlViewerResolver(Func<string, HtmlViewer?> resolver) => Default.AttachHtmlViewerResolver(resolver);

    /// <summary>解除 htmlviewer 控件解析器槽位。</summary>
    public static void DetachHtmlViewerResolver() => Default.DetachHtmlViewerResolver();

    /// <summary>宿主注入的页面内容同步回调；未注入为 null。</summary>
    public static Action<string>? HtmlContentSink => Default.HtmlContentSink;

    /// <summary>是否已注入页面内容同步回调。</summary>
    public static bool HasHtmlContentSink => Default.HasHtmlContentSink;

    /// <summary>由宿主注入页面内容同步回调（幂等：覆盖式）。传入需非 null。</summary>
    public static void AttachHtmlContentSink(Action<string> sink) => Default.AttachHtmlContentSink(sink);

    /// <summary>解除页面内容同步回调槽位。</summary>
    public static void DetachHtmlContentSink() => Default.DetachHtmlContentSink();

    /// <summary>桥链路诊断 sink；未注入为 null。</summary>
    public static Action<string, string>? DiagnosticsSink => Default.DiagnosticsSink;

    /// <summary>是否已注入桥链路诊断 sink。</summary>
    public static bool HasDiagnosticsSink => Default.HasDiagnosticsSink;

    /// <summary>由宿主注入桥链路诊断 sink（幂等：覆盖式）。传入需非 null。</summary>
    public static void AttachDiagnosticsSink(Action<string, string> sink) => Default.AttachDiagnosticsSink(sink);

    /// <summary>解除桥链路诊断 sink 槽位。</summary>
    public static void DetachDiagnosticsSink() => Default.DetachDiagnosticsSink();

    /// <summary>宿主解析出的插件 UI 配置路径；未回填为 null。</summary>
    public static string? ConfigPath => Default.ConfigPath;

    /// <summary>由宿主回填配置路径（幂等：覆盖式）。</summary>
    public static void AttachConfigPath(string? configPath) => Default.AttachConfigPath(configPath);

    /// <summary>解除配置路径槽位。</summary>
    public static void DetachConfigPath() => Default.DetachConfigPath();

    /// <summary>组合根单一装配入口（转发默认实例）。</summary>
    public static void AttachComposition(HtmlUiHostComposition composition) => Default.AttachComposition(composition);

    /// <summary>HTML 宿主装配单一入口（转发默认实例）。</summary>
    public static HtmlBridgeAssemblySnapshot ComposeHtmlHost(
        Func<string, HtmlViewer?>? viewerResolver,
        Action<string>? contentSink,
        Action<string, string>? diagnosticsSink,
        string? configPath)
        => Default.ComposeHtmlHost(viewerResolver, contentSink, diagnosticsSink, configPath);

    /// <summary>查询当前 HTML 装配清单（转发默认实例）。</summary>
    public static HtmlBridgeAssemblySnapshot GetBridgeAssemblySnapshot() => Default.GetBridgeAssemblySnapshot();
}
