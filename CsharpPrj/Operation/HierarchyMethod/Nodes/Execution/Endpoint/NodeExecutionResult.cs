using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 中立执行结果（<see cref="IExecutionEndpoint.ExecuteAsync"/> 的统一返回类型，LLM 无关）。
/// 承载通用执行语义（成败/文本/决策/错误/会话/输出态/消息数）；执行器专属富化归因
/// （Cline 的 FailureStage/EmptyReason/TokenUsage/StopReason 等）经 <see cref="Extras"/> 承载，不丢损。
/// </summary>
public sealed record NodeExecutionResult(
    string Text,
    bool Ok,
    string? Decision = null,
    string? Error = null,
    string? SessionId = null,
    string? OutputState = null,
    int MessageCount = 0,
    IReadOnlyDictionary<string, object?>? Extras = null)
{
    /// <summary>成功但无产出文本（outputState=missing / 空结果原因非空）→ 显式空结果，不得静默判成功。</summary>
    public bool IsEmpty => Ok && string.IsNullOrWhiteSpace(Text);
}
