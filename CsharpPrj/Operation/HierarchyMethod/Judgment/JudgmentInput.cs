using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>判定输入单一形状（声明 + 事实 + 能力 + 口径 + 作用域）。</summary>
public sealed record JudgmentInput(JudgmentScope Scope)
{
    public IReadOnlyList<string> DeclaredPorts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeclaredInports { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeclaredSteps { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExecutedSteps { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SkippedSteps { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ActualInportKeys { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ActualOutportKeys { get; init; } = Array.Empty<string>();

    public JudgmentFact Fact { get; init; } = JudgmentFact.Empty;

    public DeclarationCapability Capability { get; init; } = DeclarationCapability.Empty;

    public PresenceSemantics Semantics { get; init; } = PresenceSemantics.ContractDefault;

    public IReadOnlyCollection<string> ExemptKeys { get; init; } = Array.Empty<string>();
}
