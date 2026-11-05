using Modules.Node.Diagnostics;

namespace Modules.Node.Materialization.WritePlan;

/// <summary>
/// 写执行结果：applied/rejected 二态 + 已应用/已短路/已补偿清单 + 诊断（可解释、可对账）。
/// </summary>
/// <param name="Applied">是否全部步骤成功。</param>
/// <param name="PlanId">计划 Id。</param>
/// <param name="AppliedWorkIds">成功应用的步骤 Id。</param>
/// <param name="SkippedWorkIds">被短路的步骤 Id。</param>
/// <param name="CompensatedWorkIds">失败后成功补偿的步骤 Id（逆序）。</param>
/// <param name="RejectedAt">拒绝发生的步骤 Id（成功 ⇒ null）。</param>
/// <param name="Reason">拒绝原因（成功 ⇒ null）。</param>
/// <param name="Diagnostics">本次执行诊断。</param>
public sealed record NodeWriteOutcome(
    bool Applied,
    string PlanId,
    IReadOnlyList<string> AppliedWorkIds,
    IReadOnlyList<string> SkippedWorkIds,
    IReadOnlyList<string> CompensatedWorkIds,
    string? RejectedAt,
    string? Reason,
    IReadOnlyList<NodeDiagnostic> Diagnostics)
{
    /// <summary>全步骤成功。</summary>
    public static NodeWriteOutcome Ok(
        string planId,
        IReadOnlyList<string> applied,
        IReadOnlyList<string> skipped,
        IReadOnlyList<NodeDiagnostic>? diagnostics = null)
        => new(true, planId, applied, skipped, Array.Empty<string>(), null, null,
            diagnostics ?? Array.Empty<NodeDiagnostic>());

    /// <summary>计划被拒绝（任一步失败即整体拒绝，前序已应用步骤走补偿）。</summary>
    public static NodeWriteOutcome Rejected(
        string planId,
        IReadOnlyList<string> applied,
        IReadOnlyList<string> skipped,
        IReadOnlyList<string> compensated,
        string? rejectedAt,
        string? reason,
        IReadOnlyList<NodeDiagnostic>? diagnostics = null)
        => new(false, planId, applied, skipped, compensated, rejectedAt, reason,
            diagnostics ?? Array.Empty<NodeDiagnostic>());
}
