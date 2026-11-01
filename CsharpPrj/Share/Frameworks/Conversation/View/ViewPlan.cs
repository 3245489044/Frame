using System;
using System.Collections.Generic;

namespace Share.Conversation.View;

/// <summary>取数阶段（<c>gather/conversation.json</c> 的 stages[] 编译视图）。</summary>
/// <param name="Name">阶段名。</param>
/// <param name="Needs">本阶段依赖域。</param>
/// <param name="Deps">前置阶段名。</param>
/// <param name="Sections">本阶段负责的区域。</param>
public sealed record ViewStage(
    string Name,
    IReadOnlyList<string> Needs,
    IReadOnlyList<string> Deps,
    IReadOnlyList<string> Sections);

/// <summary>单视图呈现计划（主键 = 视图键）。</summary>
/// <param name="View">视图键。</param>
/// <param name="TypeKey">消息桥 data-type 键（受控词）。</param>
/// <param name="ContentDomain">内容域（编译期须能被读路径或 [Gather] 域解释）。</param>
/// <param name="ContentScope">内容作用域。</param>
/// <param name="Fold">呈现层默认折叠口径。</param>
/// <param name="Regions">区域序列（受控区域类型）。</param>
/// <param name="Stages">取数阶段序。</param>
public sealed record ViewPlanRow(
    string View,
    string TypeKey,
    string ContentDomain,
    string ContentScope,
    string Fold,
    IReadOnlyList<string> Regions,
    IReadOnlyList<ViewStage> Stages);

/// <summary>视图计划表（编译产物；区域/阶段/折叠口径单源）。</summary>
/// <param name="Views">视图 → 计划。</param>
/// <param name="Fingerprint">内容指纹。</param>
public sealed record ViewPlanTable(IReadOnlyDictionary<string, ViewPlanRow> Views, string Fingerprint)
{
    /// <summary>空表。</summary>
    public static readonly ViewPlanTable Empty =
        new(new Dictionary<string, ViewPlanRow>(StringComparer.Ordinal), string.Empty);

    /// <summary>按视图取计划（未声明 ⇒ false）。</summary>
    /// <param name="view">视图键。</param>
    /// <param name="plan">计划（未命中为 null）。</param>
    public bool TryGet(string? view, out ViewPlanRow? plan)
    {
        plan = null;
        return !string.IsNullOrWhiteSpace(view) && Views.TryGetValue(view!, out plan);
    }

    /// <summary>按视图取计划；未声明 ⇒ 抛。</summary>
    /// <param name="view">视图键。</param>
    public ViewPlanRow Require(string view)
        => TryGet(view, out var plan)
            ? plan!
            : throw new InvalidOperationException($"视图计划未声明：{view}（ViewPlan）");
}
