#nullable enable

using System.Collections.Generic;
using Share.Conversation.Presentation;
using Share.Conversation.Rendering;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 空态 / 降级**唯一出口**（禁各处自建等价结构）：
/// 空态读声明模板；降级读声明降级模板，缺该模板 ⇒ 纯文本可见（不伪造样式）。
/// <para>自 <c>OperationProjector</c> 迁出（投影器已删；本类为共享出口，独立承载）。</para>
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
