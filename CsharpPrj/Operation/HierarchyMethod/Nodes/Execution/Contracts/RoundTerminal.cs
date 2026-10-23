using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>轮终态种类（一轮恰一个；Succeeded 才允许终答推导）。</summary>
public enum RoundTerminalKind
{
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>轮终态记录（终态单源）：<see cref="Facts"/> 供观测/落库（键=事实名，值=结构事实）。</summary>
public sealed record RoundTerminal(
    RoundTerminalKind Kind,
    string? Decision,
    RoundFailure? Failure,
    IReadOnlyDictionary<string, string> Facts)
{
    public bool Succeeded => Kind == RoundTerminalKind.Succeeded;
}

/// <summary>终态策略选项：<see cref="Strictness"/> 决定契约违规姿态；<see cref="LegacyOutcomeCompatibility"/> 旧口径并行输出（R5：默认关闭，一迭代后删除）。</summary>
public sealed record RoundTerminalPolicyOptions(
    RoundContractStrictness Strictness = RoundContractStrictness.Report,
    bool LegacyOutcomeCompatibility = false)
{
    public static RoundTerminalPolicyOptions Default { get; } = new();
}
