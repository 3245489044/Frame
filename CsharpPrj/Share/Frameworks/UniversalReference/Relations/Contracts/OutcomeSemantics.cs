using UniversalWorkflowFramework.Relations.Mounting;

namespace UniversalWorkflowFramework.Relations;

/// <summary>关系/挂载结果的中性语义（跨层映射单点；领域枚举保留，本类只做归一）。</summary>
public enum OutcomeClass
{
    Ok,
    Duplicate,
    NotApplicable,
    Rejected,
    Missing,
    Unavailable,
    Partial,
    Failed,
}

/// <summary><see cref="RelationOutcome"/> / <see cref="MountOutcome"/> → <see cref="OutcomeClass"/> 唯一映射面。</summary>
public static class RelationOutcomeSemantics
{
    public static OutcomeClass Of(RelationOutcome outcome) => outcome switch
    {
        RelationOutcome.Applied => OutcomeClass.Ok,
        RelationOutcome.Duplicate => OutcomeClass.Duplicate,
        RelationOutcome.Skipped => OutcomeClass.NotApplicable,
        RelationOutcome.Rejected => OutcomeClass.Rejected,
        RelationOutcome.NotFound => OutcomeClass.Missing,
        RelationOutcome.NotAssembled => OutcomeClass.Unavailable,
        _ => OutcomeClass.Failed,
    };

    public static OutcomeClass Of(MountOutcome outcome) => outcome switch
    {
        MountOutcome.Committed => OutcomeClass.Ok,
        MountOutcome.PartiallyApplied => OutcomeClass.Partial,
        MountOutcome.NotApplicable => OutcomeClass.NotApplicable,
        MountOutcome.Missing => OutcomeClass.Missing,
        MountOutcome.Rejected => OutcomeClass.Rejected,
        MountOutcome.Failed => OutcomeClass.Failed,
        _ => OutcomeClass.Failed,
    };

    public static bool IsSuccess(OutcomeClass value)
        => value is OutcomeClass.Ok or OutcomeClass.Duplicate;
}
