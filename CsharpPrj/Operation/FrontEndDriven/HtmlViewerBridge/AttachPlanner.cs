#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Share.Conversation;
using Share.Conversation.Rendering;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>顶层锚点条目：锚点消息 + 折叠到其下拉栏的过程帧（源序）。</summary>
public sealed record AttachEntry(ConversationRenderItem Item, IReadOnlyList<ConversationRenderItem> Attach);

/// <summary>组内行（结果已并入调用行：<see cref="ResultText"/> 非空即该行承载结果）。</summary>
public sealed record AttachRowSpec(
    string Kind,
    string? Badge,
    ConversationRenderItem Source,
    string? ResultText,
    string? ResultLabel);

/// <summary>顺序组（组序 = 发生顺序；组内行序 = 源序，结果不另占行）。</summary>
public sealed record AttachGroupPlan(string Kind, string Label, IReadOnlyList<AttachRowSpec> Rows);

/// <summary>
/// 过程帧计划（**计数单源**）：<see cref="FrameCount"/> = 输入帧数，
/// <see cref="RowCount"/> = 实际呈现行数（结果嵌套后）；摘要与组计数均读本记录。
/// </summary>
public sealed record AttachPlan(int FrameCount, int RowCount, IReadOnlyList<AttachGroupPlan> Groups)
{
    /// <summary>空计划。</summary>
    public static readonly AttachPlan Empty = new(0, 0, Array.Empty<AttachGroupPlan>());
}

/// <summary>
/// 过程帧计划器（纯函数）：顶层锚点归属 + 顺序分组，唯一实现。
/// <para>归属规则：过程项挂到**其前方最近锚点**（any anchor）；无前锚 ⇒ 原位顶层（不改序）。
/// 旧“优先其后 assistant”的文本锚算法已删除（因果反转，E1/E7）。</para>
/// </summary>
public static class AttachPlanner
{
    /// <summary>顶层分组：终答过渡提升 + 前最近锚点折叠（输入恰消费一次 ⇒ 顶层 + 挂靠 = 全集）。</summary>
    public static IReadOnlyList<AttachEntry> PlanTopLevel(
        IReadOnlyList<ConversationRenderItem> items, ConversationFramePolicy? policy)
    {
        var ordered = policy is null
            ? new List<ConversationRenderItem>(items)
            : PromoteAnswer(items, policy);

        var entries = new List<AttachEntry>(ordered.Count);
        List<ConversationRenderItem>? bucket = null;
        foreach (var item in ordered)
        {
            if (ConversationAnchors.IsAnchor(item.Role))
            {
                bucket = new List<ConversationRenderItem>();
                entries.Add(new AttachEntry(item, bucket));
            }
            else if (bucket is not null)
            {
                bucket.Add(item);
            }
            else
            {
                entries.Add(new AttachEntry(item, Array.Empty<ConversationRenderItem>()));
            }
        }

        return entries;
    }

    /// <summary>过渡提升：归档 assistant 已同文（归一化）⇒ 去重；本轮最后一条过程文本 ⇒ 终答气泡。</summary>
    private static List<ConversationRenderItem> PromoteAnswer(
        IReadOnlyList<ConversationRenderItem> items, ConversationFramePolicy policy)
    {
        var result = new List<ConversationRenderItem>(items);
        if (policy.Answers.Promotion == AnswerPromotion.None) return result;

        var archived = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in result)
        {
            if (item.Role == ConversationRole.Assistant
                && !policy.IsTraceText(item.Stage)
                && policy.RoleOf(item.Stage) != FrameRole.Answer)
            {
                archived.Add(policy.Answers.Normalize(item.Text));
            }
        }

        if (policy.Answers.Promotion == AnswerPromotion.EntityOnly)
        {
            for (var i = 0; i < result.Count; i++)
            {
                var item = result[i];
                if (policy.RoleOf(item.Stage) != FrameRole.Answer) continue;
                if (archived.Contains(policy.Answers.Normalize(item.Text)))
                {
                    result.RemoveAt(i);
                    i--;
                    continue;
                }

                result[i] = Promote(item);
            }

            return result;
        }

        var last = -1;
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i].Role == ConversationRole.Tool && policy.IsTraceText(result[i].Stage)) last = i;
        }

        if (last < 0) return result;

        var candidate = result[last];
        if (archived.Contains(policy.Answers.Normalize(candidate.Text)))
        {
            result.RemoveAt(last);
            return result;
        }

        result[last] = Promote(candidate);
        return result;
    }

    private static ConversationRenderItem Promote(ConversationRenderItem item)
        => new(ConversationRole.Assistant, item.Stage, item.SubAgent, item.Text,
            item.Round, MessageChannel.Dialogue, item.Targets);

    /// <summary>顺序分组：边界开组 / 收束只收本类 / 收束后异类开新组 / 调用与结果配对。</summary>
    public static AttachPlan PlanGroups(
        IReadOnlyList<ConversationRenderItem> attach, ConversationFramePolicy policy)
    {
        var buckets = new List<Bucket>();
        Bucket? current = null;
        var iteration = 0;

        foreach (var item in attach)
        {
            var kind = item.Stage ?? string.Empty;
            var role = policy.RoleOf(kind);

            if (role == FrameRole.Boundary)
            {
                iteration++;
                current = new Bucket("iteration",
                    string.Format(CultureInfo.InvariantCulture, policy.Labels.Iteration, iteration));
                buckets.Add(current);
            }
            else if (role == FrameRole.Trailer)
            {
                if (current is null || current.Kind != "trailer")
                {
                    current = new Bucket("trailer", policy.Labels.Trailer);
                    buckets.Add(current);
                }
            }
            else if (current is null)
            {
                current = new Bucket("lead", policy.Labels.Lead);
                buckets.Add(current);
            }
            else if (current.Kind == "trailer")
            {
                // E3：收束组不吞后续异类帧 —— 新开“结算后”组，源序与语义均不复位。
                current = new Bucket("post", policy.Labels.PostTrailer);
                buckets.Add(current);
            }

            current!.Items.Add(item);
        }

        var groups = new List<AttachGroupPlan>(buckets.Count);
        var rowCount = 0;
        foreach (var bucket in buckets)
        {
            var rows = BuildRows(bucket.Items, policy);
            rowCount += rows.Count;
            groups.Add(new AttachGroupPlan(bucket.Kind, bucket.Label, rows));
        }

        return new AttachPlan(attach.Count, rowCount, groups);
    }

    /// <summary>组内行：结果并入最近调用行；无配对则独立成行（零丢失）。</summary>
    private static List<AttachRowSpec> BuildRows(
        IReadOnlyList<ConversationRenderItem> items, ConversationFramePolicy policy)
    {
        var rows = new List<AttachRowSpec>(items.Count);
        var callIndexes = new Stack<int>();

        foreach (var item in items)
        {
            var kind = item.Stage ?? string.Empty;
            var role = policy.RoleOf(kind);
            var badge = policy.BadgeOf(kind);

            if (role == FrameRole.Result && callIndexes.Count > 0)
            {
                var index = callIndexes.Pop();
                var target = rows[index];
                rows[index] = target with
                {
                    ResultText = item.Text ?? string.Empty,
                    ResultLabel = string.IsNullOrWhiteSpace(badge) ? kind : badge,
                };
                continue;
            }

            rows.Add(new AttachRowSpec(kind, badge, item, null, null));
            if (role == FrameRole.Call) callIndexes.Push(rows.Count - 1);
        }

        return rows;
    }

    private sealed class Bucket
    {
        public Bucket(string kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public string Kind { get; }
        public string Label { get; }
        public List<ConversationRenderItem> Items { get; } = new();
    }
}
