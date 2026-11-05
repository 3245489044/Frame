namespace ContractFramework.Abstractions;

/// <summary>
/// 契约违例异常（装配期 <c>FailFast</c> 的**单一抛出点**产物）。
/// <para>含违例清单 + 未判定（<c>⊥</c>）清单 + 复算引导。由 <see cref="Gate.ContractGate.Enforce"/> 唯一抛出，
/// 其它任何实现不得直接抛本类型（抛出点唯一）。</para>
/// </summary>
public sealed class ContractViolationException : InvalidOperationException
{
    public ContractViolationException(
        string message,
        IReadOnlyList<ContractViolation> violations,
        IReadOnlyList<ContractUnverifiable> unverifiable)
        : base(message)
    {
        Violations = violations ?? [];
        Unverifiable = unverifiable ?? [];
    }

    /// <summary>违例清单（结构事实证据）。</summary>
    public IReadOnlyList<ContractViolation> Violations { get; }

    /// <summary>不可判定（<c>⊥</c>）清单。</summary>
    public IReadOnlyList<ContractUnverifiable> Unverifiable { get; }

    /// <summary>构造带修复引导的消息体（供 <see cref="Gate.ContractGate.Enforce"/> 复用）。</summary>
    public static string Compose(
        IReadOnlyList<ContractViolation> violations,
        IReadOnlyList<ContractUnverifiable> unverifiable)
    {
        var lines = new List<string> { "契约装配期校验失败（fail-closed：⊥ 与 FailFast 违例一律不放行）：" };
        if (violations.Count > 0)
        {
            lines.Add($"- 违例 {violations.Count} 条：");
            lines.AddRange(violations.Select(v => "    · " + v));
        }

        if (unverifiable.Count > 0)
        {
            lines.Add($"- 不可判定（⊥）{unverifiable.Count} 条：");
            lines.AddRange(unverifiable.Select(u => "    · " + u));
        }

        lines.Add("修复口径：补齐 contracts.json 五元组 + 可复算证据；载体须为结构事实（禁语义词）；复算命令见各契约 evidence.recomputeCmd。");
        return string.Join(Environment.NewLine, lines);
    }
}
