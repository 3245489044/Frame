using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.Facts;

/// <summary>
/// 会话事实完备性（方案 §2.5）——可机检的「这一轮是否完整」，**全部机械复算**（不含 LLM 判断）。
/// <para>
/// 判据入口 = <see cref="Of"/>（由事实条目集合推导）；<see cref="PartialReason"/> 取值受控于
/// <see cref="PartialReasons"/>（声明面同源），<see cref="PartialReasons.IngestMiss"/> 为静默丢失的可见化档。
/// </para>
/// </summary>
/// <param name="HasDispatch">是否有轮次受理事实。</param>
/// <param name="HasStepResult">是否有步骤终态事实。</param>
/// <param name="HasFinalAnswer">是否有终答事实（权威级见 <see cref="FactEnvelope.Authority"/>）。</param>
/// <param name="Iterations">执行面 LLM 回合数。</param>
/// <param name="ToolCalls">工具调用数。</param>
/// <param name="ToolFailures">工具失败数（<c>ok=false</c> 的 <c>ToolResult</c> 条目数）。</param>
/// <param name="StopReason">停止原因（终答优先，其次步骤终态）。</param>
/// <param name="Partial">是否不完整。</param>
/// <param name="PartialReason">不完整原因（受控词；完整时为 <see cref="PartialReasons.None"/>）。</param>
public sealed record FactCompleteness(
    bool HasDispatch,
    bool HasStepResult,
    bool HasFinalAnswer,
    int Iterations,
    int ToolCalls,
    int ToolFailures,
    string? StopReason,
    bool Partial,
    string? PartialReason)
{
    /// <summary>是否完整（受理 + 步骤终态 + 终答 齐备且无未推进项）。</summary>
    public bool IsComplete => HasDispatch && HasStepResult && HasFinalAnswer && !Partial;

    /// <summary>由事实条目集合推导完备性（唯一实现）。</summary>
    /// <param name="facts">事实条目（可为 null = 空文档）。</param>
    public static FactCompleteness Of(IReadOnlyList<FactEnvelope>? facts)
    {
        var list = facts ?? Array.Empty<FactEnvelope>();

        var hasDispatch = false;
        var hasStepResult = false;
        FactEnvelope? finalAnswer = null;
        FactEnvelope? terminate = null;
        var iterations = 0;
        var toolCalls = 0;
        var toolFailures = 0;

        foreach (var fact in list)
        {
            switch (fact.Kind)
            {
                case FactKindNames.RoundDispatch:
                    hasDispatch = true;
                    break;
                case FactKindNames.StepResult:
                    hasStepResult = true;
                    break;
                case FactKindNames.LlmIteration:
                    iterations++;
                    break;
                case FactKindNames.ToolCall:
                    toolCalls++;
                    break;
                case FactKindNames.ToolResult:
                    if (!FactPayload.ReadBool(fact.PayloadJson, true, FactKeys.Ok)) toolFailures++;
                    break;
                case FactKindNames.FinalAnswer:
                    finalAnswer = fact;
                    break;
                case FactKindNames.RoundTerminate:
                    terminate = fact;
                    break;
            }
        }

        var stopReason = finalAnswer is not null
            ? FactPayload.ReadString(finalAnswer.PayloadJson,
                FactKeys.StopReason, FactKeys.StopReasonCompat)
            : FactPayload.ReadString(terminate?.PayloadJson,
                FactKeys.StopReason, FactKeys.StopReasonCompat);

        var (partial, reason) = DeterminePartial(finalAnswer, terminate, iterations, toolCalls);

        return new FactCompleteness(
            hasDispatch,
            hasStepResult,
            finalAnswer is not null,
            iterations,
            toolCalls,
            toolFailures,
            stopReason,
            partial,
            reason);
    }

    /// <summary>诊断串（供日志可见化：<c>complete</c> / <c>partial:reason</c>）。</summary>
    public override string ToString()
        => IsComplete ? "complete" : $"partial:{PartialReason}";

    /// <summary>
    /// 不完整判定（**判定序即语义**）：
    /// ① 轮次终止事实带受控 <c>partial_reason</c>（非 <c>none</c>）⇒ 直接采用（声明面判据优先）；
    /// ② 无终答事实 + 有执行面事实 ⇒ <c>no_final_answer</c>；无执行面事实 ⇒ <c>ingest_miss</c>（静默丢失档）；
    /// ③ 终答权威级 = <c>Streamed</c> ⇒ <c>streamed_only</c>（叙述 ≠ 交付）；
    /// ④ 终答权威级 = <c>Error</c> ⇒ 载荷 <c>partial_reason</c>（受控）否则 <c>engine_exited</c>；
    /// ⑤ 其余 ⇒ 完整。
    /// </summary>
    private static (bool Partial, string Reason) DeterminePartial(
        FactEnvelope? finalAnswer,
        FactEnvelope? terminate,
        int iterations,
        int toolCalls)
    {
        var declared = FactPayload.ReadString(terminate?.PayloadJson, FactKeys.PartialReason);
        if (PartialReasons.IsControlled(declared) && declared != PartialReasons.None)
            return (true, declared!);

        if (finalAnswer is null)
        {
            var executionFace = iterations > 0 || toolCalls > 0;
            return (true, executionFace ? PartialReasons.NoFinalAnswer : PartialReasons.IngestMiss);
        }

        if (finalAnswer.Authority == FactAuthority.Streamed) return (true, PartialReasons.StreamedOnly);

        if (finalAnswer.Authority == FactAuthority.Error)
        {
            var reason = FactPayload.ReadString(finalAnswer.PayloadJson, FactKeys.PartialReason);
            return (true, PartialReasons.IsControlled(reason) && reason != PartialReasons.None
                ? reason!
                : PartialReasons.EngineExited);
        }

        var explicitPartial = FactPayload.ReadBool(finalAnswer.PayloadJson, false, FactKeys.Partial);
        return explicitPartial
            ? (true, PartialReasons.NoFinalAnswer)
            : (false, PartialReasons.None);
    }
}
