using System;
using Share.ObjectiveBoundary;


namespace Share.ObjectiveBoundary;

/// <summary>
/// 边界重构器。把触发后的 BoundaryAssessment 动作翻译为可消费的下一步提示（ReconstructHint）
/// 与更新后的 BoundaryContext 快照（记录 LastAction/PhiOld/ReconstructCount）。它本身不写
/// AxiomContext.Data——状态写入由 DefaultBoundaryFramework 统一编排，本类保持纯计算。
/// </summary>
public static class BoundaryReconstructor
{
    public static BoundaryReconstructResult Apply(BoundaryContext current, BoundaryAssessment assessment)
        => new(
            NextContext: current.Apply(assessment, assessment.EffectiveRoot),
            Hint: BuildHint(assessment.Action));

    private static string BuildHint(string action) => action switch
    {
        // C5：显式 OrdinalIgnoreCase 比较，替代 ToLowerInvariant + 常量碰巧小写的隐性耦合
        _ when string.Equals(action, BoundaryActionKind.Compact, StringComparison.OrdinalIgnoreCase) =>
            "边界重构[compact]：上下文接近极限，已压缩历史。请聚焦当前子问题、避免重复上下文，必要时分步提交。",
        _ when string.Equals(action, BoundaryActionKind.ResubmitWithInfo, StringComparison.OrdinalIgnoreCase) =>
            "边界重构[resubmit_with_info]：当前边界信息增益趋零。请明确你需要的新输入条件（如补充日志/运行值/外部信息），等价于'改变输入条件以重构边界'。",
        _ when string.Equals(action, BoundaryActionKind.Rescope, StringComparison.OrdinalIgnoreCase) =>
            "边界重构[rescope]：问题域超出当前边界。请收窄为可独立完成、可验证的子问题，再逐子问题推进。",
        _ when string.Equals(action, BoundaryActionKind.Escalate, StringComparison.OrdinalIgnoreCase) =>
            "边界重构[escalate]：已升级边界预算。请继续推进并避免重复失败路径。",
        _ => "边界重构完成：请基于新边界继续推进。",
    };
}

/// <summary>重构产物：下一步提示 + 更新后的边界状态快照。</summary>
public sealed record BoundaryReconstructResult(BoundaryContext NextContext, string Hint);
