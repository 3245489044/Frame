#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>字段可见性三态（Known/Unknown/NotApplicable；禁把 Unknown 与 NotApplicable 压平）。</summary>
public enum UiValueState
{
    /// <summary>值可用。</summary>
    Known = 0,

    /// <summary>字段应有但缺失（可见占位 + 原因码）。</summary>
    Unknown = 1,

    /// <summary>该 kind 声明面不适用（不渲染；不是缺值）。</summary>
    NotApplicable = 2,
}

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
    /// <summary>可见性（默认 Known；未知/不适用走工厂，禁靠 Cell 猜测）。</summary>
    public UiValueState Visibility { get; init; } = UiValueState.Known;

    /// <summary>未知原因码（可见化；Known/NotApplicable 可为 null）。</summary>
    public string? UnknownReason { get; init; }

    /// <summary>维度键（占位形态单源：actor/round/cost…；可空）。</summary>
    public string? Dimension { get; init; }

    /// <summary>可见占位字段（未声明/缺失值的唯一出口；禁静默丢弃）。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="label">标签（可空）。</param>
    public static UiField Unknown(string name, string? label = null)
        => Unknown(name, label, null, null);

    /// <summary>可见占位字段（带原因/维度）。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="label">标签（可空）。</param>
    /// <param name="reason">原因码（可空 ⇒ missing-value）。</param>
    /// <param name="dimension">维度键（可空）。</param>
    public static UiField Unknown(string name, string? label, string? reason, string? dimension = null)
        => new(name, SurfaceCellIds.Unknown, null, label, Array.Empty<string>())
        {
            Visibility = UiValueState.Unknown,
            UnknownReason = reason ?? SurfaceReasonCodes.MissingValue,
            Dimension = dimension,
        };

    /// <summary>声明面不适用（不渲染；与未知区分）。</summary>
    /// <param name="name">字段名。</param>
    /// <param name="label">标签（可空）。</param>
    /// <param name="reason">原因码（可空 ⇒ not-applicable）。</param>
    public static UiField NotApplicable(string name, string? label = null, string? reason = null)
        => new(name, SurfaceCellIds.Text, null, label, Array.Empty<string>())
        {
            Visibility = UiValueState.NotApplicable,
            UnknownReason = reason ?? SurfaceReasonCodes.NotApplicable,
        };

    /// <summary>是否未知占位（只认显式 Unknown；载体单元不承载语义）。</summary>
    public bool IsUnknown => Visibility == UiValueState.Unknown;

    /// <summary>是否声明面不适用（不渲染）。</summary>
    public bool IsNotApplicable => Visibility == UiValueState.NotApplicable;
}
