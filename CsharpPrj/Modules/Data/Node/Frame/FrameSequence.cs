using System;
using System.Collections.Generic;

namespace Modules.Node.Frame;

/// <summary>帧角色受控词（结构词汇；分组/配对/计数由本轴派生，框架零领域词）。</summary>
public static class FrameRoleIds
{
    public const string Plain = "plain";
    public const string Trace = "trace";
    public const string Terminal = "terminal";
    public const string Boundary = "boundary";
    public const string Trailer = "trailer";
    public const string Call = "call";
    public const string Result = "result";
    public const string Operation = "operation";
}

/// <summary>kind → 角色标识 + 徽标（声明投影；宿主注入）。</summary>
public sealed record FrameRoleSpec(string Kind, string RoleId, string? Badge);

/// <summary>序列计划消费的分组文案（4 项；宿主注入，框架零领域词/零 UI 资产）。</summary>
public sealed record FrameGroupText(string Lead, string Iteration, string Trailer, string PostTrailer)
{
    /// <summary>结构缺省（宿主应注入产品文案）。</summary>
    public static readonly FrameGroupText Default = new("lead", "iteration {0}", "trailer", "post");
}

/// <summary>帧的中性投影输入（宿主把领域对象投影成本记录；对象本身仍由泛型参数携带）。</summary>
public sealed record FrameItem(
    string Kind,
    int Round,
    string Text = "",
    string? PresentationWord = null,
    string? Channel = null,
    bool? Terminal = null);

/// <summary>顶层锚点条目：锚点 + 折叠到其下拉栏的过程帧（源序）。</summary>
public sealed record FrameEntry<T>(T Anchor, IReadOnlyList<T> Attach);

/// <summary>组内行（结果已并入调用行：<see cref="ResultText"/> 非空即该行承载结果）。</summary>
public sealed record FrameRowSpec<T>(
    string Kind,
    string? Badge,
    T Source,
    string? ResultText,
    string? ResultLabel);

/// <summary>顺序组（组序 = 发生顺序；组内行序 = 源序）。</summary>
public sealed record FrameGroupPlan<T>(string Kind, string Label, IReadOnlyList<FrameRowSpec<T>> Rows);

/// <summary>
/// 过程帧计划（计数单源）：<see cref="FrameCount"/> = 输入帧数，<see cref="RowCount"/> = 实际呈现行数，
/// <see cref="TextCount"/> = 文本类行数，<see cref="MergedCount"/> = 并入调用行的结果帧数；
/// 摘要与组计数一律读本记录（禁二次重算）。
/// </summary>
public sealed record FrameAttachPlan<T>(
    int FrameCount,
    int RowCount,
    int TextCount,
    IReadOnlyList<FrameGroupPlan<T>> Groups,
    int MergedCount = 0)
{
    /// <summary>空计划。</summary>
    public static readonly FrameAttachPlan<T> Empty = new(0, 0, 0, Array.Empty<FrameGroupPlan<T>>(), 0);
}

/// <summary>顶层分组结果：<see cref="Lead"/> = 首锚之前的帧（段首折叠）；<see cref="Entries"/> = 锚点 + 附属（源序）。</summary>
public sealed record FrameTopLevelPlan<T>(IReadOnlyList<T> Lead, IReadOnlyList<FrameEntry<T>> Entries);

/// <summary>帧边类型（读侧派生，不落库；锚点归属 / 调用-结果 / 组内成员）。</summary>
public enum FrameEdgeType
{
    AnchorAttach,
    CallResult,
    GroupMember,
}

/// <summary>帧间关系边（由序列计划派生；可复用 <c>Relations\EdgeReachability</c> 做闭包）。</summary>
public sealed record FrameEdge(FrameEdgeType Type, string FromKind, string ToKind, string? GroupKind = null);

/// <summary>
/// 帧序列计划器（**唯一分组/配对实现**）：锚点归属 + 段首折叠 + 边界/收束分组 + 调用/结果嵌套。
/// <para>
/// 输入泛型 <typeparamref name="T"/>（宿主领域对象原样携带，避免第二套 DTO）；
/// 锚点判定与角色/徽标由构造注入（声明驱动，框架零领域词）。
/// </para>
/// </summary>
public sealed class FrameSequencer<T>
{
    private readonly IReadOnlyDictionary<string, FrameRoleSpec> _roles;
    private readonly Func<T, FrameItem> _project;
    private readonly Func<T, bool> _isAnchor;
    private readonly FrameGroupText _labels;

    /// <param name="roles">kind → 角色/徽标（声明投影；缺省 = plain/无徽标）。</param>
    /// <param name="labels">分组文案（宿主注入）。</param>
    /// <param name="project">领域对象 → 中性帧项。</param>
    /// <param name="isAnchor">气泡锚点判定（唯一判据由宿主注入）。</param>
    public FrameSequencer(
        IReadOnlyDictionary<string, FrameRoleSpec> roles,
        FrameGroupText labels,
        Func<T, FrameItem> project,
        Func<T, bool> isAnchor)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _isAnchor = isAnchor ?? throw new ArgumentNullException(nameof(isAnchor));
    }

    /// <summary>顶层分组（含前导折叠集）：事实锚点 + 前最近锚点折叠（输入恰消费一次 ⇒ 前导 + 顶层 + 挂靠 = 全集）。</summary>
    public FrameTopLevelPlan<T> Plan(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var entries = new List<FrameEntry<T>>(items.Count);
        var lead = new List<T>();
        List<T>? bucket = null;
        foreach (var item in items)
        {
            if (_isAnchor(item))
            {
                bucket = new List<T>();
                entries.Add(new FrameEntry<T>(item, bucket));
            }
            else if (bucket is not null)
            {
                bucket.Add(item);
            }
            else
            {
                lead.Add(item);
            }
        }

        return new FrameTopLevelPlan<T>(lead, entries);
    }

    /// <summary>顺序分组：边界开组 / 收束只收本类 / 收束后异类开新组 / 调用与结果配对。</summary>
    public FrameAttachPlan<T> PlanGroups(IReadOnlyList<T> attach)
    {
        ArgumentNullException.ThrowIfNull(attach);
        var buckets = new List<Bucket>();
        Bucket? current = null;
        var iteration = 0;

        foreach (var item in attach)
        {
            var kind = _project(item).Kind;
            var role = RoleOf(kind);

            if (role == FrameRoleIds.Boundary)
            {
                iteration++;
                current = new Bucket("iteration", string.Format(_labels.Iteration, iteration));
                buckets.Add(current);
            }
            else if (role == FrameRoleIds.Trailer)
            {
                if (current is null || current.Kind != "trailer")
                {
                    current = new Bucket("trailer", _labels.Trailer);
                    buckets.Add(current);
                }
            }
            else if (current is null)
            {
                current = new Bucket("lead", _labels.Lead);
                buckets.Add(current);
            }
            else if (current.Kind == "trailer")
            {
                current = new Bucket("post", _labels.PostTrailer);
                buckets.Add(current);
            }

            current!.Items.Add(item);
        }

        var groups = new List<FrameGroupPlan<T>>(buckets.Count);
        var rowCount = 0;
        var textCount = 0;
        var mergedCount = 0;
        foreach (var bucket in buckets)
        {
            var rows = BuildRows(bucket.Items);
            rowCount += rows.Count;
            textCount += rows.Count(row => IsTrace(row.Kind));
            mergedCount += bucket.Items.Count - rows.Count;
            groups.Add(new FrameGroupPlan<T>(bucket.Kind, bucket.Label, rows));
        }

        return new FrameAttachPlan<T>(attach.Count, rowCount, textCount, groups, mergedCount);
    }

    /// <summary>派生帧边（锚点归属 / 调用-结果 / 组内成员）；读侧关系，不落库。</summary>
    public IReadOnlyList<FrameEdge> DeriveEdges(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var edges = new List<FrameEdge>();
        var plan = Plan(items);
        foreach (var entry in plan.Entries)
        {
            var anchorKind = _project(entry.Anchor).Kind;
            foreach (var attached in entry.Attach)
            {
                edges.Add(new FrameEdge(FrameEdgeType.AnchorAttach, anchorKind, _project(attached).Kind));
            }

            foreach (var group in PlanGroups(entry.Attach).Groups)
            {
                foreach (var row in group.Rows)
                {
                    edges.Add(new FrameEdge(FrameEdgeType.GroupMember, group.Kind, row.Kind, group.Kind));
                }
            }

            AppendCallResultEdges(edges, entry.Attach);
        }

        return edges;
    }

    /// <summary>调用-结果配对（LIFO，与 <see cref="BuildRows"/> 同一配对口径）。</summary>
    private void AppendCallResultEdges(List<FrameEdge> edges, IReadOnlyList<T> attach)
    {
        var calls = new Stack<string>();
        foreach (var item in attach)
        {
            var kind = _project(item).Kind;
            var role = RoleOf(kind);
            if (role == FrameRoleIds.Call)
            {
                calls.Push(kind);
                continue;
            }

            if (role == FrameRoleIds.Result && calls.Count > 0)
            {
                edges.Add(new FrameEdge(FrameEdgeType.CallResult, calls.Pop(), kind));
            }
        }
    }

    /// <summary>kind → 角色标识（未登记 ⇒ plain，可见不丢）。</summary>
    public string RoleOf(string? kind)
        => kind is not null && _roles.TryGetValue(kind, out var spec) ? spec.RoleId : FrameRoleIds.Plain;

    /// <summary>kind → 徽标（未登记 ⇒ null，模板回退显示 kind）。</summary>
    public string? BadgeOf(string? kind)
        => kind is not null && _roles.TryGetValue(kind, out var spec) ? spec.Badge : null;

    /// <summary>是否过程文本（组内计数/角标用；不决定呈现身份）。</summary>
    public bool IsTrace(string? kind) => RoleOf(kind) == FrameRoleIds.Trace;

    private List<FrameRowSpec<T>> BuildRows(IReadOnlyList<T> items)
    {
        var rows = new List<FrameRowSpec<T>>(items.Count);
        var callIndexes = new Stack<int>();

        foreach (var item in items)
        {
            var frame = _project(item);
            var role = RoleOf(frame.Kind);
            var badge = BadgeOf(frame.Kind);

            if (role == FrameRoleIds.Result && callIndexes.Count > 0)
            {
                var index = callIndexes.Pop();
                var target = rows[index];
                rows[index] = target with
                {
                    ResultText = frame.Text ?? string.Empty,
                    ResultLabel = string.IsNullOrWhiteSpace(badge) ? frame.Kind : badge,
                };
                continue;
            }

            rows.Add(new FrameRowSpec<T>(frame.Kind, badge, item, null, null));
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
        public List<T> Items { get; } = new();
    }
}
