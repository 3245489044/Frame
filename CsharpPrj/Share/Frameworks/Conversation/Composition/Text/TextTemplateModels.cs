#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>缺槽策略 —— 只决定“未绑定/未知槽”的文本与结论，不改变已绑定值的语义。</summary>
public enum TemplateMissingPolicy
{
    /// <summary>原样保留 token（Produce 既有 <c>{{key}}</c> 行为）。</summary>
    Keep,

    /// <summary>替换为空串（显式空）。</summary>
    Omit,

    /// <summary>替换为可见占位，结论降级（Degraded）。</summary>
    Unknown,

    /// <summary>替换为可见占位，结论阻断（Blocked）。</summary>
    FailFast,
}

/// <summary>
/// 模板槽的一次取值 —— 槽名 + 四态 + 文本 + 原因 + 来源。
/// <para>
/// 状态是唯一语义载体：<c>Known</c>/<c>Empty</c> 才有文本；<c>NotApplicable</c> 表示本步不适用；
/// <c>Unknown</c> 表示应有但未解析出，必须可见化（与 <see cref="TextState"/> 同一口径）。
/// </para>
/// </summary>
/// <param name="Slot">槽名（模板 token）。</param>
/// <param name="State">四态。</param>
/// <param name="Text">文本（仅 Known/Empty 有意义）。</param>
/// <param name="Reason">不适用/未知的结构化原因。</param>
/// <param name="Source">提供者标识（诊断用，不参与语义）。</param>
public sealed record TemplateSlotValue(
    string Slot,
    TextState State,
    string? Text = null,
    string? Reason = null,
    string Source = "")
{
    /// <summary>已知文本（null → 空串，状态仍为 Known）。</summary>
    public static TemplateSlotValue Known(string slot, string? text, string source = "")
        => new(slot, TextState.Known, text ?? string.Empty, null, source);

    /// <summary>显式空（是否阻断由调用方策略决定）。</summary>
    public static TemplateSlotValue Empty(string slot, string? reason = null, string source = "")
        => new(slot, TextState.Empty, string.Empty, reason, source);

    /// <summary>应有但未解析出（可见化）。</summary>
    public static TemplateSlotValue Unknown(string slot, string? reason = null, string source = "")
        => new(slot, TextState.Unknown, null, reason, source);

    /// <summary>本步不适用（不计缺失，不参与未知清单）。</summary>
    public static TemplateSlotValue NotApplicable(string slot, string? reason = null, string source = "")
        => new(slot, TextState.NotApplicable, string.Empty, reason ?? "not-applicable", source);
}

/// <summary>
/// 一次模板渲染的输入。
/// <para>
/// <see cref="Scalars"/> 对应第一阶段（默认 <c>{slot}</c>）；<see cref="Blocks"/> 对应第二阶段（默认 <c>&lt;&lt;slot&gt;&gt;</c>）。
/// 两阶段由同一次扫描完成：已填入的值不会再被后续 token 识别（避免节点文本里的 JSON 大括号或 <c>&lt;&lt;&gt;&gt;</c> 被二次解析）。
/// </para>
/// </summary>
/// <param name="Template">模板串（null → 空串）。</param>
/// <param name="Scalars">标量槽（可为 null，视为空）。</param>
/// <param name="Blocks">块槽（可为 null；为 null 时第二定界符不参与扫描）。</param>
/// <param name="Missing">缺槽策略。</param>
/// <param name="MaxChars">结果最大字符数（&lt;=0 不截断）。</param>
/// <param name="UnknownPlaceholder">未知槽的可见占位。</param>
/// <param name="ScalarDelimiters">标量定界符（null → <see cref="TemplateDelimiters.Brace"/>）。</param>
/// <param name="BlockDelimiters">块定界符（null → <see cref="TemplateDelimiters.Angle"/>）。</param>
/// <param name="Rules">槽名规则（null → <see cref="TemplateSlotNameRules.Identifier"/>）。</param>
public sealed record TextTemplateRequest(
    string? Template,
    IReadOnlyDictionary<string, TemplateSlotValue>? Scalars,
    IReadOnlyDictionary<string, TemplateSlotValue>? Blocks = null,
    TemplateMissingPolicy Missing = TemplateMissingPolicy.Unknown,
    int MaxChars = 0,
    string? UnknownPlaceholder = "«unknown»",
    TemplateDelimiters? ScalarDelimiters = null,
    TemplateDelimiters? BlockDelimiters = null,
    TemplateSlotNameRules? Rules = null);

/// <summary>
/// 模板渲染结果 —— 文本 + 槽状态 + 未知清单 + 哈希 + 结论。
/// <para>结论枚举复用 <see cref="TextVerdictStatus"/>，禁止第二套 Ok/Failed 词汇。</para>
/// </summary>
/// <param name="Text">填充后的文本。</param>
/// <param name="Hash">本次渲染的稳定摘要。</param>
/// <param name="Status">结论（Complete/Degraded/Blocked）。</param>
/// <param name="Slots">模板中实际出现的槽（首次出现序）。</param>
/// <param name="UnknownSlots">缺失/未知槽（首次出现序，去重）。</param>
/// <param name="Truncated">是否发生超长截断。</param>
public sealed record TextTemplateResult(
    string Text,
    string Hash,
    TextVerdictStatus Status,
    IReadOnlyList<TemplateSlotValue> Slots,
    IReadOnlyList<string> UnknownSlots,
    bool Truncated)
{
    /// <summary>是否未被阻断（Degraded 仍可继续）。</summary>
    public bool CanProceed => Status != TextVerdictStatus.Blocked;
}
