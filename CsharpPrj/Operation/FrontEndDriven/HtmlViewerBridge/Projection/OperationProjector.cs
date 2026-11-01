#nullable enable

using System.Collections.Generic;
using System.Linq;
using Share.Conversation;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 操作流投影（唯一实现）：操作面帧逐条落顶部操作面板（非气泡）。
/// <para>模板缺失 ⇒ <see cref="Notices.Degraded"/>（可见降级），禁在桥内复刻面板结构。</para>
/// </summary>
internal static class OperationProjector
{
    /// <summary>模板缺失诊断码前缀（声明面 <c>ui/diagnostics.json</c>）。</summary>
    public const string TemplateMissingCode = "template-missing";

    /// <summary>操作面板节点。</summary>
    public static IReadOnlyList<UiNode> Project(
        IReadOnlyList<ConversationRenderItem> operations, ConversationProjectionContext context)
    {
        var panelKey = context.Surface.KeyOf(TemplateRegions.ActionPanel);
        if (!context.Surface.HasOperationPanel)
        {
            var code = TemplateMissingCode + ":"
                + (panelKey.Length > 0 ? panelKey : TemplateRegions.ActionPanel);
            return Notices.Degraded(code, context);
        }

        var rows = operations
            .OrderBy(item => item.Round)
            .Select(item => (object?)RowModels.Operation(item))
            .ToList();

        return UiFragmentWriter.Render(context.Surface.Templates, panelKey,
            new Dictionary<string, object?>
            {
                // 面板标题 = 宿主注入文案（模板 {title} 消费；桥内零领域词）。
                ["title"] = context.Labels.OperationPanelTitle,
                ["rows"] = rows,
            });
    }
}

/// <summary>
/// 空态 / 降级**唯一出口**（禁各处自建等价结构）：
/// 空态读声明模板；降级读声明降级模板，缺该模板 ⇒ 纯文本可见（不伪造样式）。
/// </summary>
internal static class Notices
{
    /// <summary>空态节点（空态模板缺失 ⇒ 结构等价空态节点，仍可见，禁静默渲空）。</summary>
    public static IReadOnlyList<UiNode> Empty(ConversationProjectionContext context)
        => context.Surface.HasEmpty
            ? UiFragmentWriter.Render(context.Surface.Templates,
                context.Surface.KeyOf(TemplateRegions.EmptyP),
                new Dictionary<string, object?> { ["text"] = context.EmptyText })
            : new[]
            {
                new UiNode("p", Attributes: new Dictionary<string, string>(System.StringComparer.Ordinal)
                {
                    ["class"] = "empty",
                }, Children: UiFragmentWriter.Texts(context.EmptyText)),
            };

    /// <summary>降级节点（声明降级模板优先；缺 ⇒ 同一契约的降级节点，原因可见）。</summary>
    public static IReadOnlyList<UiNode> Degraded(string reason, ConversationProjectionContext context)
    {
        var text = context.DegradedText(reason);
        return context.Surface.HasDegraded
            ? UiFragmentWriter.Render(context.Surface.Templates,
                context.Surface.KeyOf(TemplateRegions.RenderDegraded),
                new Dictionary<string, object?> { ["reason"] = reason, ["text"] = text })
            : new[]
            {
                new UiNode("p", Attributes: new Dictionary<string, string>(System.StringComparer.Ordinal)
                {
                    ["class"] = "render-degraded",
                    ["data-reason"] = reason,
                }, Children: UiFragmentWriter.Texts(text)),
            };
    }
}

