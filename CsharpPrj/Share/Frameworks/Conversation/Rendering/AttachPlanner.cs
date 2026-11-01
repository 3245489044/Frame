#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Share.Conversation.Presentation;
namespace Share.Conversation.Rendering;

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
/// <see cref="RowCount"/> = 实际呈现行数（结果嵌套后），<see cref="TextCount"/> = 文本类行数；
/// 摘要、组计数与测试判据均读本记录（禁二次重算）。
/// </summary>
public sealed record AttachPlan(int FrameCount, int RowCount, int TextCount, IReadOnlyList<AttachGroupPlan> Groups)
{
    /// <summary>空计划。</summary>
    public static readonly AttachPlan Empty = new(0, 0, 0, Array.Empty<AttachGroupPlan>());
}

/// <summary>
/// 顶层分组结果：<see cref="Lead"/> = 首锚**之前**的过程帧（段首折叠，禁逐条铺开）；
/// <see cref="Entries"/> = 锚点气泡 + 其下拉栏附属（源序）。
/// </summary>
public sealed record TopLevelPlan(
    IReadOnlyList<ConversationRenderItem> Lead,
    IReadOnlyList<AttachEntry> Entries);

/// <summary>
/// 过程帧计划器（纯函数）：顶层锚点归属 + 顺序分组，唯一实现。
/// <para>归属规则：过程项挂到**其前方最近锚点**；首锚之前的过程帧进 <see cref="TopLevelPlan.Lead"/>（段首折叠）。
/// 锚点判定 = **唯一呈现身份**（<see cref="ConversationRenderItem.PresentationIdentity"/> ∈ Bubble/Answer/Notice）：
/// 过程/操作帧不是锚点，不得冒充气泡；禁再按 Terminal/Role/Channel 二次推断。</para>
/// </summary>
public static class AttachPlanner
{
    /// <summary>
    /// 气泡锚点判定（**唯一判据 = 声明面的结构面**）：只有 <see cref="PresentationSurface.Bubble"/> 可以进入对话气泡面。
    /// </summary>
    /// <param name="item">渲染项。</param>
    /// <param name="declaration">呈现声明（产品策略）。</param>
    public static bool IsBubbleAnchor(ConversationRenderItem item, PresentationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(declaration);
        var (classId, _) = PresentationResolver.Resolve(
            declaration, item.Role, item.Channel, item.Presentation, item.Terminal);
        return declaration.IsBubbleSurface(classId);
    }

    /// <summary>顶层分组（含前导折叠集）：事实锚点 + 前最近锚点折叠（输入恰消费一次 ⇒ 前导 + 顶层 + 挂靠 = 全集）。</summary>
    public static TopLevelPlan Plan(
        IReadOnlyList<ConversationRenderItem> items, FramePolicy? policy)
    {
        var entries = new List<AttachEntry>(items.Count);
        var lead = new List<ConversationRenderItem>();
        var declaration = policy?.Presentation ?? PresentationDeclaration.Empty;
        List<ConversationRenderItem>? bucket = null;
        foreach (var item in items)
        {
            if (IsBubbleAnchor(item, declaration))
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
                // 首个锚点之前：进段首折叠（旧行为是逐条顶层 ⇒ 上方全铺开）。
                lead.Add(item);
            }
        }

        return new TopLevelPlan(lead, entries);
    }

    /// <summary>顺序分组：边界开组 / 收束只收本类 / 收束后异类开新组 / 调用与结果配对。</summary>
    public static AttachPlan PlanGroups(
        IReadOnlyList<ConversationRenderItem> attach, FramePolicy policy)
    {
        var buckets = new List<Bucket>();
        Bucket? current = null;
        var iteration = 0;

        foreach (var item in attach)
        {
            var kind = item.Stage ?? string.Empty;
            var role = policy.RoleOf(kind);

            if (role == FrameRoleIds.Boundary)
            {
                iteration++;
                current = new Bucket("iteration",
                    string.Format(CultureInfo.InvariantCulture, policy.Labels.Iteration, iteration));
                buckets.Add(current);
            }
            else if (role == FrameRoleIds.Trailer)
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
        var textCount = 0;
        foreach (var bucket in buckets)
        {
            var rows = BuildRows(bucket.Items, policy);
            rowCount += rows.Count;
            textCount += rows.Count(row => policy.IsTrace(row.Kind));
            groups.Add(new AttachGroupPlan(bucket.Kind, bucket.Label, rows));
        }

        return new AttachPlan(attach.Count, rowCount, textCount, groups);
    }

    /// <summary>组内行：结果并入最近调用行；无配对则独立成行（零丢失）。</summary>
    private static List<AttachRowSpec> BuildRows(
        IReadOnlyList<ConversationRenderItem> items, FramePolicy policy)
    {
        var rows = new List<AttachRowSpec>(items.Count);
        var callIndexes = new Stack<int>();

        foreach (var item in items)
        {
            var kind = item.Stage ?? string.Empty;
            var role = policy.RoleOf(kind);
            var badge = policy.BadgeOf(kind);

            if (role == FrameRoleIds.Result && callIndexes.Count > 0)
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
            if (role == FrameRoleIds.Call) callIndexes.Push(rows.Count - 1);
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
