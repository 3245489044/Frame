#nullable enable

using System.Collections.Generic;
using System.Linq;
using Share.Conversation;
using Share.Conversation.Rendering;
using UiBackend.Abstractions;
using UiBackend.Html;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 头提示层投影（唯一实现）：prompt:* 段整体前置为会话级块（round=0），不参与轮分组。
/// </summary>
internal static class HeadBandProjector
{
    /// <summary>头提示层节点（模板缺失 ⇒ 空，调用方已按能力判定）。</summary>
    public static IReadOnlyList<UiNode> Project(
        IReadOnlyList<ConversationRenderItem> head, ConversationProjectionContext context)
    {
        var model = new Dictionary<string, object?>
        {
            ["round"] = "0",
            ["roundTitle"] = context.Labels.SessionScope,
            ["messages"] = head
                .Select((item, index) => (object?)RowModels.Head(item, index))
                .ToList(),
        };

        return UiFragmentWriter.Render(context.Surface.Templates, "sectionHeader", model);
    }
}
