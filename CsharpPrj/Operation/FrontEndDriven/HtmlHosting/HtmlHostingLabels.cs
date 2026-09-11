#nullable enable

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用宿主门面的诊断文案标签 —— 桥诊断串按插件参数化，使通用层不写死任何插件名（宿主名 / 注册库名 / 页面称谓 /
/// 配置路径属性名），同时允许消费方逐字保留既有文案（行为等价闸口：诊断串逐字一致）。
/// 默认值即通用中性文案；插件按需覆盖 <see cref="HostName"/> 等项即可。
/// </summary>
public sealed record HtmlHostingLabels
{
    /// <summary>宿主门面类型名（诊断串用；缺省通用名）。</summary>
    public string HostName { get; init; } = "HtmlUiHost";

    /// <summary>函数注册库名（诊断串用；缺省通用名）。</summary>
    public string LibraryName { get; init; } = "HtmlBridgeLibrary";

    /// <summary>页面称谓（诊断串用，如「会话页」）。</summary>
    public string PageNoun { get; init; } = "页面";

    /// <summary>配置路径属性名（诊断串用）。</summary>
    public string ConfigPathPropertyName { get; init; } = "ConfigPath";

    /// <summary>整串覆盖：未装配提示（缺省由 HostName 合成）。</summary>
    public string? NotAttachedTemplate { get; init; }

    /// <summary>整串覆盖：htmlviewer 控件未解析提示（缺省由 HostName/PageNoun 合成）。</summary>
    public string? ViewerUnresolvedTemplate { get; init; }

    /// <summary>整串覆盖：函数注册库未装配提示（缺省由 LibraryName 合成）。</summary>
    public string? RegistryNotAttachedTemplate { get; init; }

    /// <summary>整串覆盖：配置路径未就绪提示（占位 <c>{0}</c> 为实际路径；缺省由 HostName/ConfigPathPropertyName 合成）。</summary>
    public string? ConfigPathNotReadyTemplate { get; init; }

    /// <summary>未装配 HTML 后端时的诊断文案。</summary>
    public string NotAttached => NotAttachedTemplate
        ?? $"（HTML 后端未装配）请宿主创建 HtmlBackendRuntime 并调用 {HostName}.AttachHtmlUi(...)。";

    /// <summary>htmlviewer 控件未解析时的诊断文案。</summary>
    public string ViewerUnresolved => ViewerUnresolvedTemplate
        ?? $"（htmlviewer 控件未解析）请宿主注入 {HostName}.AttachHtmlViewerResolver 并在{PageNoun} sideload 后调用";

    /// <summary>函数注册库未装配时的诊断文案。</summary>
    public string RegistryNotAttached => RegistryNotAttachedTemplate
        ?? $"（函数注册库未装配）请先经 FunctionLibraryLoader 注册 {LibraryName}";

    /// <summary>配置路径未就绪时的诊断文案（注入实际路径）。</summary>
    public string ConfigPathNotReady(string? configPath) =>
        (ConfigPathNotReadyTemplate ?? $"（配置路径未就绪）{HostName}.{ConfigPathPropertyName}={{0}}")
        .Replace("{0}", configPath ?? "null");

    /// <summary>装配成功摘要（含就绪报告与两项注入状态）。</summary>
    public string AttachedSummary(string readinessSummary, bool templatesAttached, bool pageDeclarationAttached)
        => $"HTML 后端已装配: 就绪[ {readinessSummary} ] | 模板注入={templatesAttached}, {PageNoun}声明注入={pageDeclarationAttached}";

    /// <summary>Agent 插件既有文案（逐字保留；行为等价闸口要求）。</summary>
    public static HtmlHostingLabels Agent { get; } = new()
    {
        HostName = "AgentUiHost",
        LibraryName = "AgentUiLibrary",
        PageNoun = "会话页",
        ConfigPathPropertyName = "AgentUiConfigPath",
    };
}
