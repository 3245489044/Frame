using System.Collections.Generic;
using UniversalWorkflowFramework.Relations.Mounting;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary><see cref="RelationOutcome"/> / <see cref="MountOutcome"/> → <see cref="OutcomeClass"/> 唯一映射面。</summary>
public static class RelationOutcomeSemantics
{
    // 映射表（非 switch）：新增终态 = 增一行；未映射值可被 TryOf 判出（禁静默落 Failed 冒充判定）。
    private static readonly IReadOnlyDictionary<RelationOutcome, OutcomeClass> RelationMap =
        new Dictionary<RelationOutcome, OutcomeClass>
        {
            [RelationOutcome.Applied] = OutcomeClass.Ok,
            [RelationOutcome.Duplicate] = OutcomeClass.Duplicate,
            [RelationOutcome.Skipped] = OutcomeClass.NotApplicable,
            [RelationOutcome.Rejected] = OutcomeClass.Rejected,
            [RelationOutcome.NotFound] = OutcomeClass.Missing,
            [RelationOutcome.NotAssembled] = OutcomeClass.Unavailable,
        };

    private static readonly IReadOnlyDictionary<MountOutcome, OutcomeClass> MountMap =
        new Dictionary<MountOutcome, OutcomeClass>
        {
            [MountOutcome.Committed] = OutcomeClass.Ok,
            [MountOutcome.PartiallyApplied] = OutcomeClass.Partial,
            [MountOutcome.NotApplicable] = OutcomeClass.NotApplicable,
            [MountOutcome.Missing] = OutcomeClass.Missing,
            [MountOutcome.Rejected] = OutcomeClass.Rejected,
            [MountOutcome.Failed] = OutcomeClass.Failed,
        };

    public static OutcomeClass Of(RelationOutcome outcome)
        => RelationMap.TryGetValue(outcome, out var value) ? value : OutcomeClass.Failed;

    /// <summary>未映射判定（补枚举值时用于负对照；调用方须显式处理 false）。</summary>
    public static bool TryOf(RelationOutcome outcome, out OutcomeClass value)
        => RelationMap.TryGetValue(outcome, out value);

    public static OutcomeClass Of(MountOutcome outcome)
        => MountMap.TryGetValue(outcome, out var value) ? value : OutcomeClass.Failed;

    /// <summary>未映射判定（补枚举值时用于负对照；调用方须显式处理 false）。</summary>
    public static bool TryOf(MountOutcome outcome, out OutcomeClass value)
        => MountMap.TryGetValue(outcome, out value);

    public static bool IsSuccess(OutcomeClass value)
        => value is OutcomeClass.Ok or OutcomeClass.Duplicate;

    /// <summary>受控词（跨层出线唯一形态；禁调用方各自拼字符串）。</summary>
    public static string Wire(OutcomeClass value) => value switch
    {
        OutcomeClass.Ok => "ok",
        OutcomeClass.Duplicate => "duplicate",
        OutcomeClass.NotApplicable => "not-applicable",
        OutcomeClass.Rejected => "rejected",
        OutcomeClass.Missing => "missing",
        OutcomeClass.Unavailable => "unavailable",
        OutcomeClass.Partial => "partial",
        _ => "failed",
    };
}
