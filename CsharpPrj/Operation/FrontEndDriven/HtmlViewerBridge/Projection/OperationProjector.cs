#nullable enable

using System.Collections.Generic;
using System.Linq;
using Share.Conversation;
using Share.Conversation.Rendering;
using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 操作流投影（唯一实现）：角色=operation 的帧逐条落顶部操作面板（非气泡）。
/// <para>模板缺失 ⇒ <see cref="Notices.Degraded"/>（可见降级），禁在桥内复刻面板结构。</para>
/// </summary>
internal static class OperationProjector
{
    /// <summary>操作面板节点。</summary>
    public static IReadOnlyList<UiNode> Project(
        IReadOnlyList<ConversationRenderItem> operations, ConversationProjectionContext context)
    {
        if (!context.Surface.HasOperationPanel)
            return Notices.Degraded("template-missing:actionPanel", context);

        var rows = operations
            .OrderBy(item => item.Round)
            .Select(item => (object?)RowModels.Operation(item))
            .ToList();

        return FragmentTemplates.Render(context.Surface.Templates, "actionPanel",
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
/// 空态读声明模板；降级读 <c>renderDegraded</c>，缺该模板 ⇒ 纯文本可见（不伪造样式）。
/// </summary>
internal static class Notices
{
    /// <summary>空态节点（<c>emptyP</c> 缺失 ⇒ 结构等价空态节点，仍可见，禁静默渲空）。</summary>
    public static IReadOnlyList<UiNode> Empty(ConversationProjectionContext context)
        => context.Surface.HasEmpty
            ? FragmentTemplates.Render(context.Surface.Templates, "emptyP",
                new Dictionary<string, object?> { ["text"] = context.EmptyText })
            : new[]
            {
                new UiNode("p", Attributes: new Dictionary<string, string>(System.StringComparer.Ordinal)
                {
                    ["class"] = "empty",
                }, Children: FragmentTemplates.Texts(context.EmptyText)),
            };

    /// <summary>降级节点（<c>renderDegraded</c> 优先；缺 ⇒ 同一契约的降级节点，原因可见）。</summary>
    public static IReadOnlyList<UiNode> Degraded(string reason, ConversationProjectionContext context)
    {
        var text = context.DegradedText(reason);
        return context.Surface.HasDegraded
            ? FragmentTemplates.Render(context.Surface.Templates, "renderDegraded",
                new Dictionary<string, object?> { ["reason"] = reason, ["text"] = text })
            : new[]
            {
                new UiNode("p", Attributes: new Dictionary<string, string>(System.StringComparer.Ordinal)
                {
                    ["class"] = "render-degraded",
                    ["data-reason"] = reason,
                }, Children: FragmentTemplates.Texts(text)),
            };
    }
}
