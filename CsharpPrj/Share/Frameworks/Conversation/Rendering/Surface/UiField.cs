#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 一个结构化字段（**禁压平**）：值是原始形态（字符串/数值/对象/数组），
/// 由后端在最后一次投影时才合成字符串。
/// </summary>
/// <param name="Name">字段名（字段词表键）。</param>
/// <param name="Cell">渲染单元（<see cref="SurfaceCellIds"/>）。</param>
/// <param name="Value">原始值（null = 缺失，调用方须生成 unknown 条目）。</param>
/// <param name="Label">已本地化标签（可空）。</param>
/// <param name="Caps">该单元需要的能力（受控词：wrap/copy/columns…）。</param>
public sealed record UiField(
    string Name,
    string Cell,
    object? Value,
    string? Label = null,
    IReadOnlyList<string>? Caps = null)
{
    /// <summary>可见占位字段（未声明/缺失值的唯一出口；禁静默丢弃）。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="label">标签（可空）。</param>
    public static UiField Unknown(string name, string? label = null)
        => new(name, SurfaceCellIds.Unknown, null, label, Array.Empty<string>());

    /// <summary>是否未知占位。</summary>
    public bool IsUnknown => string.Equals(Cell, SurfaceCellIds.Unknown, StringComparison.Ordinal);
}
