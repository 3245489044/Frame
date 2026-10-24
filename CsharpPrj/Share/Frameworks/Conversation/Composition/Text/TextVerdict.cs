#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>契约判定结论（只出结论，不出动作 —— 动作由消费方/终态策略决定）。</summary>
public enum TextVerdictStatus
{
    /// <summary>全部必填成立。</summary>
    Complete,

    /// <summary>可执行但有未知项（可见化降级）。</summary>
    Degraded,

    /// <summary>必填缺失/声明与实现不一致（不得进入执行体）。</summary>
    Blocked,
}

/// <summary>
/// 判定结论 —— 缺失/未知/不适用/派生四类清单 + 原因。
/// <para>四类清单互斥：同一端口只落一类，避免「既报缺失又报不适用」的归因歧义。</para>
/// </summary>
/// <param name="Status">总结论。</param>
/// <param name="Missing">应有但缺失/不可空（含声明适用但实现判不适用）。</param>
/// <param name="Unknown">可见化未知（可选段未解析出）。</param>
/// <param name="NotApplicable">本步不适用（不计缺失）。</param>
/// <param name="Derived">经别名派生成立。</param>
/// <param name="Reasons">结构化原因（键:原因）。</param>
public sealed record TextVerdict(
    TextVerdictStatus Status,
    IReadOnlyList<TextKey> Missing,
    IReadOnlyList<TextKey> Unknown,
    IReadOnlyList<TextKey> NotApplicable,
    IReadOnlyList<TextKey> Derived,
    IReadOnlyList<string> Reasons)
{
    /// <summary>无契约时的通过结论。</summary>
    public static TextVerdict Complete { get; } = new(
        TextVerdictStatus.Complete,
        Array.Empty<TextKey>(),
        Array.Empty<TextKey>(),
        Array.Empty<TextKey>(),
        Array.Empty<TextKey>(),
        Array.Empty<string>());

    /// <summary>是否可进入执行体（非阻断）。</summary>
    public bool CanProceed => Status != TextVerdictStatus.Blocked;
}
