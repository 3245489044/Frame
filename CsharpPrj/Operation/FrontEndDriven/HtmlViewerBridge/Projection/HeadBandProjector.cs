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
/// 头提示层投影（唯一实现）：声明段整体前置为会话级块（round=0），不参与轮分组。
/// </summary>
internal static class HeadBandProjector
{
    /// <summary>头提示层节点（模板缺失 ⇒ 空，调用方已按能力判定）。</summary>
    /// <para>声明 <c>grouping=none</c>（<see cref="FoldPolicy.SuppressHeader"/>）⇒ 只出行、不产会话级段头，
    /// 且行内轮次字段置空（视图不展示轮次）。</para>
    public static IReadOnlyList<UiNode> Project(
        IReadOnlyList<ConversationRenderItem> head, ConversationProjectionContext context)
    {
        var rows = head
            .Select((item, index) => (object?)RowModels.Head(item, index, showRound: !context.Fold.SuppressHeader))
            .ToList();

        if (context.Fold.SuppressHeader)
            return RenderRows(rows, context);

        var model = new Dictionary<string, object?>
        {
            ["round"] = "0",
            ["roundTitle"] = context.Labels.SessionScope,
            ["messages"] = rows,
        };

        return UiFragmentWriter.Render(context.Surface.Templates,
            context.Surface.KeyOf(TemplateRegions.SectionHeader), model);
    }

    /// <summary>扁平行渲染（无段头；行模板缺失 ⇒ 空，调用方已按能力判定）。</summary>
    private static IReadOnlyList<UiNode> RenderRows(
        IReadOnlyList<object?> rows, ConversationProjectionContext context)
    {
        var rowKey = context.Surface.KeyOf(TemplateRegions.Row);
        var nodes = new List<UiNode>(rows.Count);
        foreach (var row in rows)
        {
            if (row is IReadOnlyDictionary<string, object?> model)
                nodes.AddRange(UiFragmentWriter.Render(context.Surface.Templates, rowKey, model));
        }

        return nodes;
    }
}

