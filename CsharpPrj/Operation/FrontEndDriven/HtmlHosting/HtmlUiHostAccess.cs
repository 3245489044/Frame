#nullable enable

using System;
using System.Collections.Generic;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 基于 <see cref="HtmlUiHostSlot"/> 实例的端口实现（当前装配源适配器；组合根显式化后可由宿主替换为实例承载）。
/// 逐个槽实时读取，保持既有「晚装配 / 重装配即生效」语义。机制层只依赖 <see cref="IHtmlUiHostAccess"/>，不反引静态宿主。
/// </summary>
/// <summary>
/// 基于 <see cref="HtmlUiHostSlot"/> 实例的端口实现（当前装配源适配器；组合根显式化后可由宿主替换为实例承载）。
/// 逐个槽实时读取，保持既有「晚装配 / 重装配即生效」语义。机制层只依赖 <see cref="IHtmlUiHostAccess"/>，不反引静态宿主。
/// </summary>
public sealed class HtmlUiHostAccess : IHtmlUiHostAccess
{
    /// <summary>默认宿主槽（<see cref="HtmlUiHost.Default"/>）的共享适配器（单插件兼容形态）。</summary>
    public static readonly HtmlUiHostAccess Instance = new(HtmlUiHost.Default);

    private readonly HtmlUiHostSlot _host;

    public HtmlUiHostAccess(HtmlUiHostSlot host)
        => _host = host ?? throw new ArgumentNullException(nameof(host));

    public HtmlBackendRuntime? HtmlUi => _host.HtmlUi;
    public IReadOnlyDictionary<string, string>? Templates => _host.Templates;
    public UIConfiguration? PageDeclaration => _host.PageDeclaration;
    public Action<string, string>? DiagnosticsSink => _host.DiagnosticsSink;
    public Func<string, HtmlViewer?>? HtmlViewerResolver => _host.HtmlViewerResolver;
    public Action<string>? HtmlContentSink => _host.HtmlContentSink;
    public string? ConfigPath => _host.ConfigPath;
    public bool HasHtmlUi => _host.HasHtmlUi;
}
