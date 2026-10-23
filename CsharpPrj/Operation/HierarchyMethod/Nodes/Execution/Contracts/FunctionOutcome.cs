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
    public static FunctionOutcome Of(IReadOnlyDictionary<string, object?> ports)
        => new(FunctionOutcomeKind.Produced, ports);

    public static FunctionOutcome Failed(RoundFailure failure)
        => new(FunctionOutcomeKind.Failed, new Dictionary<string, object?>(StringComparer.Ordinal), failure);

    public static FunctionOutcome Skipped()
        => new(FunctionOutcomeKind.Skipped, new Dictionary<string, object?>(StringComparer.Ordinal));
}
