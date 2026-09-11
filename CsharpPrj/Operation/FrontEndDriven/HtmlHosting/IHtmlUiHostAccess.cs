#nullable enable

using System;
using System.Collections.Generic;
using Plate.HtmlViewer;
using TConfig;
using UiBackend.Html;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// HTML 宿主装配态只读端口（通用只读面）——HTML 后端机制（渲染 / 事件分发 / 脚本 / 装配）经本端口读取宿主装配的
/// 运行时、模板、页面声明与宿主注入的回调槽，不再反引任何插件静态宿主。
/// 槽位均在装配后（可晚于注册）填充；端口为**实时读取语义**，保留「晚装配 / 重装配即生效」。
/// 端口不带函数名前缀 / 页面名 / 控件名——那些是装配与注册时的参数，不属于端口。
/// </summary>
public interface IHtmlUiHostAccess
{
    /// <summary>HTML 后端运行时（DOM 桥 + 事件/脚本桥）；未装配为 null。</summary>
    HtmlBackendRuntime? HtmlUi { get; }

    /// <summary>页面壳 / 片段 HTML 模板集（配置 JSON templates 注入）；未装配为 null。</summary>
    IReadOnlyDictionary<string, string>? Templates { get; }

    /// <summary>宿主页面内部声明；未装配为 null（渲染器回退内嵌默认）。</summary>
    UIConfiguration? PageDeclaration { get; }

    /// <summary>桥链路诊断 sink（category,message → 日志门面）；未装配为 null（可选观测）。</summary>
    Action<string, string>? DiagnosticsSink { get; }

    /// <summary>按控件名解析宿主 htmlviewer；未装配为 null。</summary>
    Func<string, HtmlViewer?>? HtmlViewerResolver { get; }

    /// <summary>宿主→页面内容写回 sink（双通道 host writer）；未装配为 null。</summary>
    Action<string>? HtmlContentSink { get; }

    /// <summary>插件 UI 配置路径（装配读模板/声明用）；未装配为 null。</summary>
    string? ConfigPath { get; }

    /// <summary>是否已装配 HTML 后端运行时。</summary>
    bool HasHtmlUi { get; }
}
