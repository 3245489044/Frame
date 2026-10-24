#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>段的需求量（是否必须有值）。</summary>
public enum SegmentRequirement
{
    /// <summary>必填：未知/空且不可空 ⇒ 缺失（契约判定为阻断）。</summary>
    Required,

    /// <summary>可选：未知 ⇒ 可见化降级，不阻断。</summary>
    Optional,
}

/// <summary>
/// 段声明 —— 键/组/序/适用性/取值/需求量/可空/别名/开关/提供者的<b>唯一声明点</b>。
/// <para>契约（必填端口集、可空豁免、别名归一）全部由本声明派生，禁止在流程里手写第二份。</para>
/// </summary>
/// <param name="Key">段键（全局唯一）。</param>
/// <param name="Group">投喂组标签（不透明）。</param>
/// <param name="Order">组内序键。</param>
/// <param name="Applicable">适用性判定（false ⇒ <see cref="TextState.NotApplicable"/>，且不调用 <paramref name="Resolve"/>）。</param>
/// <param name="Resolve">取值（产四态；不得以 null/空串承载「不适用」）。</param>
/// <param name="Requirement">需求量。</param>
/// <param name="AllowEmpty">显式空是否合法（原 nullable 语义）。</param>
/// <param name="Aliases">别名（缺失时可替代的来源，顺序即优先级）。</param>
/// <param name="Toggle">开关名（null = 恒启用）。</param>
/// <param name="Provider">提供者标识（诊断用）。</param>
public sealed record TextSpec(
    TextKey Key,
    string Group,
    OrderKey Order,
    Func<ISegmentContext, bool> Applicable,
    Func<ISegmentContext, TextBlock> Resolve,
    SegmentRequirement Requirement = SegmentRequirement.Optional,
    bool AllowEmpty = false,
    IReadOnlyList<string>? Aliases = null,
    string? Toggle = null,
    string Provider = "")
{
    /// <summary>恒适用的声明快捷构造。</summary>
    public static TextSpec Always(
        TextKey key,
        string group,
        OrderKey order,
        Func<ISegmentContext, TextBlock> resolve,
        SegmentRequirement requirement = SegmentRequirement.Optional,
        bool allowEmpty = false,
        IReadOnlyList<string>? aliases = null,
        string? toggle = null,
        string provider = "")
        => new(key, group, order, static _ => true, resolve, requirement, allowEmpty, aliases, toggle, provider);
}
