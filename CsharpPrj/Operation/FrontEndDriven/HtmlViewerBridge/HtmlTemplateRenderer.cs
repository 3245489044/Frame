#nullable enable

using System.Collections.Generic;
using Share.Composition.Ports;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 模板渲染端口适配器 —— <see cref="ITemplateRenderer"/> 委托 plate 的 <see cref="HtmlTemplateEngine"/>（纯 1:1 转发）。
/// 使组装内核 <c>Share.Composition</c> 在<b>不引用 Plate.HtmlViewer</b> 的前提下复用既有 HTML 模板引擎；
/// 无状态、线程安全，可共享单例。
/// <para>落位说明：本适配器是 Conversation 组装内核 × plate(HtmlTemplateEngine) 的连接层胶水，
/// 与同目录 <see cref="HtmlStreamRenderer"/> 同一姿态（plate 不引用 Share.Conversation，方向由本层承担）。</para>
/// </summary>
public sealed class HtmlTemplateRenderer : ITemplateRenderer
{
    /// <summary>共享实例（无状态纯转发）。</summary>
    public static readonly HtmlTemplateRenderer Default = new();

    /// <inheritdoc />
    public string Render(
        IReadOnlyDictionary<string, string> templates,
        string name,
        IReadOnlyDictionary<string, object?>? model,
        string fallback = "")
        => HtmlTemplateEngine.Render(templates, name, model, fallback);

    /// <inheritdoc />
    public string RenderTemplate(
        string? template,
        IReadOnlyDictionary<string, string>? templates,
        IReadOnlyDictionary<string, object?>? model)
        => HtmlTemplateEngine.RenderTemplate(template ?? string.Empty, templates, model);
}
