using ContractFramework.Abstractions;

namespace ContractFramework.Gate;

/// <summary>
/// 元契约（Contract Well-Formedness）——契约**自身**也要合格，本框架的自举判据（文档 §2.4）。
/// <code>
/// wellformed(C) ↔ C.Id≠∅ ∧ C.Semantics≠∅ ∧ C.Carrier∈StructuralFact
///               ∧ C.Tier∈{Build,Assembly,Gate,Runtime} ∧ C.Effect∈{FailFast,Block,Heal,Log}
///               ∧ C.Evidence.recomputeCmd≠∅ ∧ 有正/负对照样本
/// ¬wellformed(C) → verdict(C) ≡ ⊥（不参与放行，进入 ContractUnverifiable 清单）
/// </code>
/// </summary>
public static class ContractWellFormedness
{
    /// <summary>元契约判定（返回不合格原因清单；空 = 合格）。</summary>
    public static IReadOnlyList<string> Violations(ContractDescriptor? contract)
    {
        var list = new List<string>();
        if (contract is null)
        {
            list.Add("契约为空");
            return list;
        }

        if (contract.Id.IsEmpty)
        {
            list.Add("缺 Id");
        }

        if (string.IsNullOrWhiteSpace(contract.Semantics))
        {
            list.Add("缺 Semantics");
        }

        if (contract.Carrier is null)
        {
            list.Add("缺 Carrier");
        }
        else if (!contract.Carrier.IsStructural)
        {
            list.Add("Carrier 非结构事实（种类未识别 / 无事实 / 含语义词）");
        }

        if (contract.Tier is null)
        {
            list.Add("缺/非法 Tier");
        }

        if (contract.Effect is null)
        {
            list.Add("缺/非法 Effect");
        }

        if (contract.Evidence is null)
        {
            list.Add("缺 Evidence");
        }
        else if (!contract.Evidence.IsComplete)
        {
            list.Add("Evidence 不完整（须 recomputeCmd 非空 ∧ 有正/负对照样本）");
        }

        return list;
    }

    /// <summary>元契约判定（附不合格原因）。</summary>
    public static bool IsWellFormed(ContractDescriptor? contract, out string reason)
    {
        var violations = Violations(contract);
        reason = violations.Count == 0 ? string.Empty : string.Join("；", violations);
        return violations.Count == 0;
    }

    /// <summary>元契约判定（仅布尔）。</summary>
    public static bool IsWellFormed(ContractDescriptor? contract) => IsWellFormed(contract, out _);
}
