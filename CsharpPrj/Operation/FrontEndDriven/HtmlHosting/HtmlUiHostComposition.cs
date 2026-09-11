#nullable enable

using System;
using System.Collections.Generic;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 宿主组合根快照 —— 把 <see cref="HtmlUiHost"/> 散落的 HTML/渲染相关静态槽位收敛为
/// **单一装配入口的不可变产物**。宿主只需一次构造快照 → <see cref="HtmlUiHost.AttachComposition"/>，
/// 消除「宿主记得调每个 AttachXxx」的人肉接线 / 能力孤岛；缺任一绑定经
/// <see cref="HtmlUiHost.GetBridgeAssemblySnapshot"/> 可查询断言。
/// </summary>
public sealed record HtmlUiHostComposition(
    HtmlBackendRuntime? HtmlUi = null,
    IReadOnlyDictionary<string, string>? Templates = null,
    UIConfiguration? PageDeclaration = null,
    Func<string, HtmlViewer?>? HtmlViewerResolver = null,
    Action<string>? HtmlContentSink = null,
    Action<string, string>? DiagnosticsSink = null,
    string? ConfigPath = null);
