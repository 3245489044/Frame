#nullable enable

namespace Share.Composition.Text;

/// <summary>
/// 段的一次取值 —— 值 + 状态 + 原因 + 来源（可追溯，不参与语义）。
/// <para>携带 <see cref="Group"/>/<see cref="Order"/>：值本身知道自己「落到哪一组、按什么序」，
/// 使装配与快照无需回头查声明表（声明仍是唯一权威，值只是其投影）。</para>
/// </summary>
/// <param name="Key">段键。</param>
/// <param name="Group">不透明组标签（域侧给 <c>system</c>/<c>user</c>/<c>body</c> 等）。</param>
/// <param name="Order">组内序键。</param>
/// <param name="State">四态。</param>
/// <param name="Text">文本（仅 <see cref="TextState.Known"/>/<see cref="TextState.Empty"/> 有意义）。</param>
/// <param name="Reason">不适用/未知的结构化原因（可见化依据）。</param>
/// <param name="Source">提供者标识（诊断/追踪用）。</param>
public sealed record TextBlock(
    TextKey Key,
    string Group,
    OrderKey Order,
    TextState State,
    string Text,
    string? Reason,
    string Source)
{
    /// <summary>是否参与拼接（Known/Empty；NotApplicable/Unknown 不产文本）。</summary>
    public bool IsUsable => State is TextState.Known or TextState.Empty;

    /// <summary>已知有文本。</summary>
    public static TextBlock Filled(TextKey key, string? text, string source = "")
        => new(key, string.Empty, OrderKey.Zero, TextState.Known, text ?? string.Empty, null, source);

    /// <summary>显式空（是否合法由声明的 AllowEmpty 裁决）。</summary>
    public static TextBlock Blank(TextKey key, string? reason = null, string source = "")
        => new(key, string.Empty, OrderKey.Zero, TextState.Empty, string.Empty, reason, source);

    /// <summary>不适用（不是缺失）。</summary>
    public static TextBlock NotApplicable(TextKey key, string? reason = null, string source = "")
        => new(key, string.Empty, OrderKey.Zero, TextState.NotApplicable, string.Empty, reason, source);

    /// <summary>未知（可见化，不得静默吞掉）。</summary>
    public static TextBlock UnknownOf(TextKey key, string? reason = null, string source = "")
        => new(key, string.Empty, OrderKey.Zero, TextState.Unknown, string.Empty, reason, source);
}
