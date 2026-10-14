namespace ContractFramework.Abstractions;

/// <summary>
/// 单契约裁决明细（供账本 / 诊断 / 反查）。
/// </summary>
public sealed record ContractOutcome(
    ContractId ContractId,
    ContractTier? Tier,
    ContractEffect? Effect,
    ContractVerdict Verdict,
    IReadOnlyList<ContractViolation> Violations,
    string? Reason)
{
    public override string ToString()
        => $"{ContractId} → {Verdict}" + (Reason is null ? string.Empty : $"（{Reason}）");
}

/// <summary>
/// 契约执行结果（三值裁决 + 违例清单 + 未判定清单 + 逐契约明细）。
/// </summary>
public sealed class ContractResult
{
    private ContractResult(
        ContractVerdict verdict,
        IReadOnlyList<ContractViolation> violations,
        IReadOnlyList<ContractUnverifiable> unverifiable,
        IReadOnlyList<ContractOutcome> outcomes)
    {
        Verdict = verdict;
        Violations = violations;
        Unverifiable = unverifiable;
        Outcomes = outcomes;
    }

    /// <summary>聚合裁决（逐契约合取：⊥ 吸收 F 吸收 T）。</summary>
    public ContractVerdict Verdict { get; }

    /// <summary>违例清单（结构事实证据）。</summary>
    public IReadOnlyList<ContractViolation> Violations { get; }

    /// <summary>不可判定清单（<c>⊥</c>）。</summary>
    public IReadOnlyList<ContractUnverifiable> Unverifiable { get; }

    /// <summary>逐契约明细。</summary>
    public IReadOnlyList<ContractOutcome> Outcomes { get; }

    /// <summary>是否放行（仅 <c>T</c> 放行）。</summary>
    public bool Released => ContractVerdictLogic.IsReleased(Verdict);

    /// <summary>空结果（无契约参与，判 <c>T</c>）。</summary>
    public static ContractResult Vacuous { get; } =
        new(ContractVerdict.True, [], [], []);

    /// <summary>由逐契约明细聚合。</summary>
    public static ContractResult From(IEnumerable<ContractOutcome> outcomes)
    {
        var list = outcomes?.ToArray() ?? [];
        var verdict = list.Aggregate(ContractVerdict.True, (acc, o) => ContractVerdictLogic.Conjunction(acc, o.Verdict));
        var violations = list.SelectMany(o => o.Violations).ToArray();
        var unverifiable = list
            .Where(o => o.Verdict == ContractVerdict.Bottom)
            .Select(o => new ContractUnverifiable(o.ContractId, o.Reason ?? "不可判定"))
            .ToArray();
        return new ContractResult(verdict, violations, unverifiable, list);
    }
}
