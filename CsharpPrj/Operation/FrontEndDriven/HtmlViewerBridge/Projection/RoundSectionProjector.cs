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
/// 轮段投影（唯一实现）：折叠策略 → 前导折叠 + 锚点行 + 轮段模板。
/// <para>能力缺失（无轮折叠键）⇒ 平铺定位（前导帧原位顶层，不丢帧、不静默换语义）；</para>
/// <para>能力缺失（无前导折叠键）⇒ 前导行以行模板直出（可见）。</para>
/// <para>模板键一律取自声明（<see cref="TemplateRegions"/> → <see cref="ConversationProjectionContext.Surface"/>）。</para>
/// </summary>
internal static class RoundSectionProjector
{
    /// <summary>单轮段节点。</summary>
    public static IReadOnlyList<UiNode> Project(
        IGrouping<int, ConversationRenderItem> group,
        int latestRound,
        ConversationProjectionContext context)
    {
        var round = group.Key;
        var items = new List<ConversationRenderItem>(group);
        var templates = context.Surface.Templates;
        var roundFoldKey = context.Surface.KeyOf(TemplateRegions.RoundFold);
        var open = context.Fold.IsOpen(round, latestRound);

        if (!context.Surface.HasRoundFold)
            return Flat(items, round, open, context);

        var plan = AttachPlanner.Plan(items, context.Policy);
        var leadRows = LeadRows(plan.Lead, round, context);
        var anchorRows = plan.Entries
            .Select((entry, index) => (object?)AttachProjector.AnchorRow(entry, round, index, context))
            .ToList();

        var model = new Dictionary<string, object?>
        {
            ["round"] = round.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["roundTitle"] = context.RoundTitle(round),
            ["open"] = open ? "1" : string.Empty,
            ["messages"] = anchorRows,
            ["costLabel"] = context.CostLabel(round),
        };

        if (!context.Surface.HasLeadFold)
        {
            // 无前导折叠能力：前导行以行模板直出（可见，不聚合 HTML 串）。
            var roundNodes = UiFragmentWriter.Render(templates, roundFoldKey, model);
            return leadRows.Count == 0
                ? roundNodes
                : UiFragmentWriter.Concat(BlockNodes(templates, leadRows, context), roundNodes);
        }

        model["leadFolds"] = leadRows.Count > 0
            ? new List<object?> { LeadFold(plan.Lead, leadRows, round, context) }
            : new List<object?>();
        return UiFragmentWriter.Render(templates, roundFoldKey, model);
    }

    /// <summary>无轮折叠能力：前导帧与锚点按合并序顶层呈现（等价旧定位，帧零丢失）。</summary>
    private static IReadOnlyList<UiNode> Flat(
        IReadOnlyList<ConversationRenderItem> items, int round, bool open, ConversationProjectionContext context)
    {
        var plan = AttachPlanner.Plan(items, context.Policy);
        var merged = new List<AttachEntry>(plan.Lead.Count + plan.Entries.Count);
        foreach (var item in plan.Lead)
            merged.Add(new AttachEntry(item, System.Array.Empty<ConversationRenderItem>()));
        merged.AddRange(plan.Entries);

        var model = new Dictionary<string, object?>
        {
            ["round"] = round.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["roundTitle"] = context.RoundTitle(round),
            ["open"] = open ? "1" : string.Empty,
            ["messages"] = merged
                .Select((entry, index) => (object?)AttachProjector.AnchorRow(entry, round, index, context))
                .ToList(),
            ["costLabel"] = context.CostLabel(round),
        };

        return UiFragmentWriter.Render(context.Surface.Templates,
            context.Surface.KeyOf(TemplateRegions.SectionHeader), model);
    }

    /// <summary>段首前导行（折叠承载：清空 tool/prompt 位）。</summary>
    private static List<object?> LeadRows(
        IReadOnlyList<ConversationRenderItem> lead, int round, ConversationProjectionContext context)
    {
        var rows = new List<object?>(lead.Count);
        for (var index = 0; index < lead.Count; index++)
        {
            rows.Add(RowModels.Message(
                lead[index], context, isAnchor: false, key: FragmentKeys.Lead(round, index), blankProcessFlags: true));
        }

        return rows;
    }

    /// <summary>前导折叠模型（数据；模板 leadFold 消费），摘要含项数与文本类计数（V3）。</summary>
    private static Dictionary<string, object?> LeadFold(
        IReadOnlyList<ConversationRenderItem> source,
        List<object?> rows,
        int round,
        ConversationProjectionContext context)
    {
        var textCount = AttachProjector.CountTrace(source, context.Policy);
        return new Dictionary<string, object?>
        {
            ["round"] = round.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["leadSummary"] = string.Join(string.Empty, new[]
            {
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    context.Labels.LeadSummaryFormat, context.Labels.Lead, source.Count),
                AttachProjector.TextCountSuffix(textCount, context),
            }),
            ["lead"] = rows,
        };
    }

    /// <summary>行模型 → 节点（模板缺失 ⇒ 空，调用方已按能力判定）。</summary>
    private static IReadOnlyList<UiNode> BlockNodes(
        IReadOnlyDictionary<string, string> templates,
        IReadOnlyList<object?> rows,
        ConversationProjectionContext context)
    {
        var rowKey = context.Surface.KeyOf(TemplateRegions.Row);
        var nodes = new List<UiNode>(rows.Count);
        foreach (var row in rows)
        {
            if (row is IReadOnlyDictionary<string, object?> model)
                nodes.AddRange(UiFragmentWriter.Render(templates, rowKey, model));
        }

        return nodes;
    }
}
