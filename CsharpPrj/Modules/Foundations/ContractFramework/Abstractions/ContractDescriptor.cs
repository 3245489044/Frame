namespace ContractFramework.Abstractions;

/// <summary>
/// 契约声明记录（五元组 + 证据）。
/// <para><c>Contract ≜ ⟨Id, Semantics, Carrier, Tier, Effect⟩ ⊗ Evidence</c>，另附陈述性元数据（Invariant/Title/Source）。</para>
/// <para>字段可空以承载"声明不完整"事实——是否合格由 <see cref="Gate.ContractWellFormedness"/> 裁定，缺任一项 ⇒ 裁决 <c>⊥</c>。</para>
/// </summary>
public sealed class ContractDescriptor
{
    public ContractDescriptor(
        ContractId id,
        string? semantics,
        ContractCarrier? carrier,
        ContractTier? tier,
        ContractEffect? effect,
        ContractHostRef? host,
        ContractEvidence? evidence,
        string? invariant = null,
        string? title = null,
        string? source = null)
    {
        Id = id ?? ContractId.Empty;
        Semantics = semantics;
        Carrier = carrier;
        Tier = tier;
        Effect = effect;
        Host = host;
        Evidence = evidence;
        Invariant = invariant;
        Title = title;
        Source = source;
    }

    /// <summary>契约标识。</summary>
    public ContractId Id { get; }

    /// <summary>不变量族 + 违规谓词（可判定的语义描述）。</summary>
    public string? Semantics { get; }

    /// <summary>判据载体（结构事实）。</summary>
    public ContractCarrier? Carrier { get; }

    /// <summary>执行层级（缺 = 未声明，判 <c>⊥</c>）。</summary>
    public ContractTier? Tier { get; }

    /// <summary>处置（缺 = 未声明，判 <c>⊥</c>）。</summary>
    public ContractEffect? Effect { get; }

    /// <summary>执行体引用（缺 = 无执行体，执行时判 <c>⊥</c>）。</summary>
    public ContractHostRef? Host { get; }

    /// <summary>可复算证据。</summary>
    public ContractEvidence? Evidence { get; }

    /// <summary>不变量族号（陈述性；如 <c>INV-1</c>）。</summary>
    public string? Invariant { get; }

    /// <summary>标题（陈述性）。</summary>
    public string? Title { get; }

    /// <summary>声明来源（文件路径 / 行，供诊断）。</summary>
    public string? Source { get; }

    public override string ToString() => $"{Id} [{Tier?.ToString() ?? "?"}/{Effect?.ToString() ?? "?"}]";
}
