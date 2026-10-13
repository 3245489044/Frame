#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 稳定键单一源（L1 通用契约）——「可保持状态节点」的稳定键属性名集**唯一定义点**；
/// 由有序属性名集构成，优先序 = 出现序（靠前者优先命中）。HTML 侧（<c>HtmlKey</c>）为其具体实例。
/// </summary>
public sealed class UiNodeKey
{
    /// <summary>构造稳定键集（属性名有序）。</summary>
    public UiNodeKey(IReadOnlyList<string> attributes)
        => Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));

    /// <summary>稳定键属性名（有序，优先序即序列序）。</summary>
    public IReadOnlyList<string> Attributes { get; }

    /// <summary>从属性集派生稳定键（首个命中即返回 <c>attr=value</c>；无命中返回 null）。</summary>
    public string? Derive(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null) return null;
        foreach (var attr in Attributes)
        {
            if (attributes.TryGetValue(attr, out var v) && !string.IsNullOrEmpty(v))
                return attr + "=" + v;
        }
        return null;
    }

    /// <summary>从节点派生稳定键。</summary>
    public string? Derive(UiNode node) => Derive(node.Attributes);
}
