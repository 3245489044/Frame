#nullable enable

using System.Collections.Generic;
using Share.Composition.Text;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 段声明行（<c>[Segment]</c> 的领域投影）—— 段键/组/序/适用性/需求量/可空/别名/开关的<b>纯数据</b>视图。
/// <para>
/// 本类型只承载声明事实，不含取值逻辑（取值仍在 <see cref="PromptSegmentCatalog"/> 的域内解析表）：
/// 声明面（<c>Config/prompt-segments.ushar</c>）→ 本行 → 计划/契约/前置门，三处共用同一份事实。
/// </para>
/// </summary>
/// <param name="Key">段键（全局唯一）。</param>
/// <param name="Group">投喂组标签（Kernel 单组不变量：<c>system</c>）。</param>
/// <param name="Order">组内序键（= CacheBand 升序值）。</param>
/// <param name="When">适用性词（<see cref="WhenVocabulary"/>；null/always = 恒适用）。</param>
/// <param name="Requirement">需求量。</param>
/// <param name="AllowEmpty">显式空是否合法。</param>
/// <param name="Aliases">兼容来源键（顺序即优先级）。</param>
/// <param name="Toggle">档案开关名（null = 恒启用）。</param>
/// <param name="Provider">提供者标识（诊断用）。</param>
/// <param name="ExpandBands">是否 L0 分段族模板行（按档案 bands 展开）。</param>
public sealed record SegmentDeclRow(
    string Key,
    string Group,
    int Order,
    string? When = null,
    SegmentRequirement Requirement = SegmentRequirement.Optional,
    bool AllowEmpty = false,
    IReadOnlyList<string>? Aliases = null,
    string? Toggle = null,
    string Provider = "",
    bool ExpandBands = false)
{
    /// <summary>结构等价（用于 Override 幂等判定；忽略取值委托）。</summary>
    public bool SameAs(SegmentDeclRow other)
        => other is not null
           && string.Equals(Key, other.Key, System.StringComparison.Ordinal)
           && string.Equals(Group, other.Group, System.StringComparison.Ordinal)
           && Order == other.Order
           && string.Equals(When ?? string.Empty, other.When ?? string.Empty, System.StringComparison.Ordinal)
           && Requirement == other.Requirement
           && AllowEmpty == other.AllowEmpty
           && string.Equals(Toggle ?? string.Empty, other.Toggle ?? string.Empty, System.StringComparison.Ordinal)
           && ExpandBands == other.ExpandBands
           && string.Equals(Describe(Aliases), Describe(other.Aliases), System.StringComparison.Ordinal);

    private static string Describe(IReadOnlyList<string>? aliases)
        => aliases is null ? string.Empty : string.Join(",", aliases);
}
