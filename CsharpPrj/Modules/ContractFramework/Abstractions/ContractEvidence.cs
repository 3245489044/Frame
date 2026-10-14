namespace ContractFramework.Abstractions;

/// <summary>
/// 可复算证据（<c>recomputeCmd</c> + 正/负对照样本）。
/// <para>权威: axioms <c>AX-FRAME.CriterionWellFormedness</c>（判据须含 <c>recompute</c>，缺则判 <c>⊥</c>）。
/// 三者齐备方算完整；缺任一 ⇒ 该契约 <c>⊥</c> 并进 <c>ContractUnverifiable</c>。</para>
/// </summary>
public sealed class ContractEvidence
{
    private ContractEvidence(string recomputeCmd, string? positive, string? negative)
    {
        RecomputeCmd = recomputeCmd;
        Positive = positive;
        Negative = negative;
    }

    /// <summary>可复算命令（非空）。</summary>
    public string RecomputeCmd { get; }

    /// <summary>正对照样本（合法应放行）。</summary>
    public string? Positive { get; }

    /// <summary>负对照样本（违规必命中）。</summary>
    public string? Negative { get; }

    /// <summary>是否完整：<c>recomputeCmd</c> 非空 ∧ 有正/负对照样本。</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(RecomputeCmd)
        && !string.IsNullOrWhiteSpace(Positive)
        && !string.IsNullOrWhiteSpace(Negative);

    /// <summary>缺失证据占位。</summary>
    public static ContractEvidence Missing { get; } = new(string.Empty, null, null);

    /// <summary>宽容构造。</summary>
    public static ContractEvidence Create(string? recomputeCmd, string? positive, string? negative)
        => new((recomputeCmd ?? string.Empty).Trim(), Trim(positive), Trim(negative));

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
