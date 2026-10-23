using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>函数结果三态（治「空结果即缺端口」）：Produced 才进入端口判定；Failed 带码；Skipped 登记为合法跳过。</summary>
public enum FunctionOutcomeKind
{
    Produced,
    Failed,
    Skipped,
}

/// <summary>函数结果三态记录：<see cref="Failure"/> 在 <see cref="FunctionOutcomeKind.Failed"/> 时必填。</summary>
public sealed record FunctionOutcome(
    FunctionOutcomeKind Kind,
    IReadOnlyDictionary<string, object?> Ports,
    RoundFailure? Failure = null)
{
    /// <summary>步骤级契约结论（事实上报；动作由轮级 Apply 统一裁决，本层不 throw）。</summary>
    public RoundContractVerdict? ContractVerdict { get; init; }

    public static FunctionOutcome Of(IReadOnlyDictionary<string, object?> ports)
        => new(FunctionOutcomeKind.Produced, ports);

    public static FunctionOutcome Failed(RoundFailure failure)
        => new(FunctionOutcomeKind.Failed, new Dictionary<string, object?>(StringComparer.Ordinal), failure);

    public static FunctionOutcome Skipped()
        => new(FunctionOutcomeKind.Skipped, new Dictionary<string, object?>(StringComparer.Ordinal));
}
