#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>端口规格 —— 由段声明派生的一条契约项（不在流程里手写）。</summary>
/// <param name="Key">规范键。</param>
/// <param name="Requirement">需求量。</param>
/// <param name="AllowEmpty">显式空是否合法。</param>
/// <param name="Aliases">别名（顺序即优先级）。</param>
/// <param name="Applicable">适用性谓词（判定「声明适用但实现判不适用」的不一致）。</param>
public sealed record PortSpec(
    TextKey Key,
    SegmentRequirement Requirement,
    bool AllowEmpty,
    IReadOnlyList<string> Aliases,
    Func<ISegmentContext, bool> Applicable);

/// <summary>端口契约 —— 段声明的派生视图；装配、前置门、快照共用同一份。</summary>
public sealed record TextContract(IReadOnlyList<PortSpec> Ports)
{
    /// <summary>空契约（判定恒 Complete）。</summary>
    public static TextContract Empty { get; } = new(Array.Empty<PortSpec>());

    /// <summary>从段声明派生契约（唯一派生点）。</summary>
    public static TextContract From(IEnumerable<TextSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var ports = new List<PortSpec>();
        foreach (var spec in specs)
        {
            ports.Add(new PortSpec(
                spec.Key,
                spec.Requirement,
                spec.AllowEmpty,
                spec.Aliases ?? Array.Empty<string>(),
                spec.Applicable));
        }
        return new TextContract(ports);
    }

    /// <summary>按键取端口规格（不存在 → null）。</summary>
    public PortSpec? PortOf(TextKey key) => Ports.FirstOrDefault(p => p.Key == key);
}
