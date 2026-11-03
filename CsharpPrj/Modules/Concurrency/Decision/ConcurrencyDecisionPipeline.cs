using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>SplitPlan 结构校验：有界、字段非空、组内无依赖。返回 null = 合格。</summary>
public static class SplitPlanValidator
{
    public static string? Validate(SplitPlan? plan, int maxMembers)
    {
        if (plan is null) return "PLAN_NULL";
        if (plan.Members is null || plan.Members.Count == 0) return "PLAN_EMPTY";
        if (plan.Members.Count > Math.Clamp(maxMembers, 1, 32)) return "PLAN_TOO_MANY_MEMBERS";
        if (string.IsNullOrWhiteSpace(plan.InputDigest)) return "PLAN_INPUT_DIGEST_EMPTY";
        for (var i = 0; i < plan.Members.Count; i++)
        {
            var member = plan.Members[i];
            if (string.IsNullOrWhiteSpace(member.Goal)) return $"PLAN_MEMBER_GOAL_EMPTY:{i}";
            if (member.DependsOn is { Count: > 0 }) return $"PLAN_MEMBER_HAS_DEPENDENCY:{i}";
            if (member.EstTokens <= 0) return $"PLAN_MEMBER_EST_TOKENS_UNKNOWN:{i}";
        }
        return null;
    }
}

/// <summary>L0 单入口决策结果。Assess → Plan → Validate → Gate → 落 plan 节点。</summary>
public sealed record ConcurrencyDecision(
    ComplexityAssessment Assessment,
    SplitPlan Plan,
    ConcurrencyAdmission? Admission,
    bool ShouldSplit,
    string Reason);

/// <summary>L0 管线（唯一实现；消费方不得自行拼装评估/拆分/成本比较）。</summary>
public sealed class ConcurrencyDecisionPipeline
{
    private readonly IComplexityAssessor _assessor;
    private readonly ISplitPlanner _planner;
    private readonly IConcurrencyBudgetGate _gate;
    private readonly ISplitPlanStore? _planStore;
    private readonly int _maxMembers;

    public ConcurrencyDecisionPipeline(
        IComplexityAssessor assessor,
        ISplitPlanner planner,
        IConcurrencyBudgetGate gate,
        ISplitPlanStore? planStore = null,
        int maxMembers = 8)
    {
        _assessor = assessor ?? throw new ArgumentNullException(nameof(assessor));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _planStore = planStore;
        _maxMembers = Math.Clamp(maxMembers, 1, 32);
    }

    public async Task<ConcurrencyDecision> DecideAsync(ComplexityInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var assessment = await _assessor.AssessAsync(input, ct).ConfigureAwait(false);
        var plan = await _planner.PlanAsync(assessment, input, ct).ConfigureAwait(false);

        var invalid = SplitPlanValidator.Validate(plan, _maxMembers);
        if (invalid is not null)
            return new ConcurrencyDecision(assessment, plan, null, false, invalid);

        if (plan.Members.Count <= 1)
            return new ConcurrencyDecision(assessment, plan, null, false, "IDENTITY");

        // P0-5（ERR-08）：未落节点的计划一律拒绝拆分——不可追溯的拆分不得发生。
        if (_planStore is null)
            return new ConcurrencyDecision(assessment, plan, null, false, "CONCURRENCY_PLAN_NOT_PERSISTED");
        if (!await _planStore.SaveAsync(plan, ct).ConfigureAwait(false))
            return new ConcurrencyDecision(assessment, plan, null, false, "CONCURRENCY_PLAN_NOT_PERSISTED");

        var admission = await _gate.EvaluateAsync(plan.Members, plan.SingleEstimateTokens, ct).ConfigureAwait(false);
        return new ConcurrencyDecision(assessment, plan, admission, admission.Admitted, admission.Code);
    }
}
