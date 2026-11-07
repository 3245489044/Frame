#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Modules.Node.Frame;
using Share.Conversation;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 折叠行投影（唯一实现）：归属计划 → 组/摘要/行模型。
/// <para>计数单源 = <c>FrameAttachPlan&lt;ConversationRenderItem&gt;</c>（FrameCount / RowCount / TextCount）；
/// 禁投影器重算，也禁测试另立计分牌。</para>
/// </summary>
internal static class AttachProjector
{
    /// <summary>锚点行（气泡 + 下拉栏）；<paramref name="groupIndex"/> 参与派生稳定键。</summary>
    public static Dictionary<string, object?> AnchorRow(
        FrameEntry<ConversationRenderItem> entry, int round, int groupIndex, ConversationProjectionContext context)
    {
        var key = FragmentKeys.Message(round, groupIndex);
        var model = RowModels.Message(entry.Anchor, context, context.IsAnchor(entry.Anchor), key);
        if (entry.Attach.Count == 0) return model;

        // 折叠门控位（模板 {?attachCount} 的唯一判据）；摘要文案随后按能力覆盖。
        model["attachCount"] = entry.Attach.Count.ToString(CultureInfo.InvariantCulture);

        if (context.Policy is null)
        {
            // 无语义表 ⇒ 扁平下拉栏（结构等价回退）。
            model["attachSummary"] = string.Format(
                CultureInfo.InvariantCulture,
                context.Labels.AttachGroupCountFormat,
                context.Labels.AttachLabel,
                entry.Attach.Count);
            model["attach"] = entry.Attach
                .Select((item, index) => (object?)RowModels.AttachRow(
                    new FrameRowSpec<ConversationRenderItem>(item.Stage ?? string.Empty, null, item, null, null),
                    FragmentKeys.Attach(round, groupIndex, index),
                    string.Empty,
                    context))
                .ToList<object?>();
            return model;
        }

        var plan = AttachPlanner.PlanGroups(entry.Attach, context.Policy);
        model["attachGroups"] = Groups(plan, round, groupIndex, context);
        model["attachSummary"] = Summary(plan, context);
        return model;
    }

    /// <summary>组行模型（键由渲染层派生；结果行内嵌，不另占行）。</summary>
    public static List<Dictionary<string, object?>> Groups(
        FrameAttachPlan<ConversationRenderItem> plan, int round, int groupIndex, ConversationProjectionContext context)
    {
        var groups = new List<Dictionary<string, object?>>(plan.Groups.Count);
        for (var group = 0; group < plan.Groups.Count; group++)
        {
            var spec = plan.Groups[group];
            var rows = new List<object?>(spec.Rows.Count);
            for (var row = 0; row < spec.Rows.Count; row++)
            {
                rows.Add(RowModels.AttachRow(
                    spec.Rows[row],
                    FragmentKeys.AttachRow(round, groupIndex, group, row),
                    spec.Kind,
                    context));
            }

            groups.Add(new Dictionary<string, object?>
            {
                ["key"] = FragmentKeys.Group(round, groupIndex, group),
                ["group"] = spec.Kind,
                ["label"] = spec.Label,
                ["count"] = spec.Rows.Count.ToString(CultureInfo.InvariantCulture),
                ["items"] = rows,
            });
        }

        return groups;
    }

    /// <summary>下拉栏摘要（计数单源 = <c>FrameAttachPlan&lt;ConversationRenderItem&gt;</c>）：组计数之和 ≡ 摘要条数；
    /// 帧数仅在「帧 ≠ 行」时补充；文本类计数恒显（V3）。</summary>
    public static string Summary(FrameAttachPlan<ConversationRenderItem> plan, ConversationProjectionContext context)
    {
        var labels = context.Labels;
        var groups = string.Join(" / ", plan.Groups.Select(group =>
            string.Format(CultureInfo.InvariantCulture, labels.AttachGroupCountFormat, group.Label, group.Rows.Count)));
        var parts = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, labels.AttachSummaryFormat, labels.AttachLabel, plan.RowCount, groups),
        };
        if (plan.RowCount != plan.FrameCount)
            parts.Add(string.Format(CultureInfo.InvariantCulture, labels.AttachSummaryFramesFormat, plan.FrameCount));
        if (plan.TextCount > 0)
            parts.Add(string.Format(CultureInfo.InvariantCulture, labels.AttachSummaryTextFormat, plan.TextCount));
        return string.Join(string.Empty, parts);
    }

    /// <summary>文本类计数（唯一实现；<see cref="AttachPlanner.PlanGroups"/> 与本类摘要共用）。</summary>
    public static int CountTrace(IReadOnlyList<ConversationRenderItem> items, FramePolicy? policy)
        => policy is null ? 0 : items.Count(item => policy.IsTrace(item.Stage));

    /// <summary>文本类计数后缀（摘要用；0 ⇒ 空串）。</summary>
    public static string TextCountSuffix(int count, ConversationProjectionContext context)
        => count > 0
            ? string.Format(CultureInfo.InvariantCulture, context.Labels.AttachSummaryTextFormat, count)
            : string.Empty;
}
