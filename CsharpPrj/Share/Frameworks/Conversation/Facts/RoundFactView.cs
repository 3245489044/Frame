using System;

namespace Share.Conversation.Facts;

/// <summary>
/// GUI「这一轮事实」视图（方案 §2.3 <c>ToRoundView</c>）——一轮的机械复算摘要，
/// 供界面单点回答「这一轮发生了什么」（iterations / tools / stop / final / token / authority / partial）。
/// <para>全部字段由事实文档投影，**不含 LLM 判断**；缺失即 null（不伪造 0 与 idle）。</para>
/// </summary>
/// <param name="RoundKey">轮键文本。</param>
/// <param name="SessionKey">会话键。</param>
/// <param name="RoundNo">轮号。</param>
/// <param name="Mode">调度模式（act / plan）。</param>
/// <param name="TermKind">轮次终止类别（受控词）。</param>
/// <param name="DispatchedAt">受理时刻。</param>
/// <param name="TerminatedAt">终止时刻。</param>
/// <param name="ElapsedMs">耗时（毫秒；载荷有则取载荷，否则由时刻机械复算）。</param>
/// <param name="StepCount">工作流步骤数。</param>
/// <param name="Iterations">执行面 LLM 回合数。</param>
/// <param name="ToolCalls">工具调用数。</param>
/// <param name="ToolFailures">工具失败数。</param>
/// <param name="ErrorCount">错误分类事实数。</param>
/// <param name="StopReason">停止原因。</param>
/// <param name="ResultSource">结果来源（sink / disk / endpoint…）。</param>
/// <param name="ResultKind">结果种类（受控词）。</param>
/// <param name="Authority">终答权威级（无终答时为 null）。</param>
/// <param name="FinalTextLength">终答文本字符数。</param>
/// <param name="TokensIn">输入 token。</param>
/// <param name="TokensOut">输出 token。</param>
/// <param name="Partial">是否不完整。</param>
/// <param name="PartialReason">不完整原因（受控词）。</param>
/// <param name="IsComplete">是否完整。</param>
public sealed record RoundFactView(
    string RoundKey,
    string SessionKey,
    int RoundNo,
    string? Mode,
    string? TermKind,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? TerminatedAt,
    double? ElapsedMs,
    int StepCount,
    int Iterations,
    int ToolCalls,
    int ToolFailures,
    int ErrorCount,
    string? StopReason,
    string? ResultSource,
    string? ResultKind,
    FactAuthority? Authority,
    int FinalTextLength,
    long? TokensIn,
    long? TokensOut,
    bool Partial,
    string? PartialReason,
    bool IsComplete)
{
    /// <summary>由事实文档投影（唯一实现；缺失即 null）。</summary>
    /// <param name="doc">事实文档。</param>
    public static RoundFactView Of(FactDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var dispatch = doc.LastOf(FactKindNames.RoundDispatch);
        var terminate = doc.LastOf(FactKindNames.RoundTerminate);
        var final = doc.FinalAnswer;
        var cost = doc.LastOf(FactKindNames.CostMeasured);
        var completeness = doc.Completeness;

        var elapsed = FactPayload.ReadDouble(terminate?.PayloadJson, FactKeys.ElapsedMs);
        if (elapsed is null && dispatch is not null && terminate is not null)
            elapsed = (terminate.AtUtc - dispatch.AtUtc).TotalMilliseconds;

        var resultSource = FactPayload.ReadString(final?.PayloadJson, FactKeys.ResultSource)
            ?? FactPayload.ReadString(doc.LastOf(FactKindNames.StepResult)?.PayloadJson,
                FactKeys.ResultSource);

        return new RoundFactView(
            doc.Key.ToString(),
            doc.Key.SessionKey,
            doc.Key.RoundNo,
            FactPayload.ReadString(dispatch?.PayloadJson, FactKeys.Mode),
            FactPayload.ReadString(terminate?.PayloadJson, FactKeys.TermKind),
            dispatch?.AtUtc,
            terminate?.AtUtc,
            elapsed,
            doc.CountOf(FactKindNames.StepStart),
            completeness.Iterations,
            completeness.ToolCalls,
            completeness.ToolFailures,
            doc.CountOf(FactKindNames.ErrorClassified),
            completeness.StopReason,
            resultSource,
            final is null
                ? null
                : FactPayload.ReadString(final.PayloadJson, FactKeys.ResultKind),
            final?.Authority,
            final is null ? 0 : FinalTextLengthOf(final),
            FactPayload.ReadLong(cost?.PayloadJson, FactKeys.TokensIn),
            FactPayload.ReadLong(cost?.PayloadJson, FactKeys.TokensOut),
            completeness.Partial,
            completeness.PartialReason,
            completeness.IsComplete);
    }

    /// <summary>终答文本长度（优先 <c>text_len</c> 判据；否则回退文本实长）。</summary>
    private static int FinalTextLengthOf(FactEnvelope final)
    {
        var declared = FactPayload.ReadLong(final.PayloadJson, FactKeys.TextLen);
        if (declared is not null) return (int)Math.Max(0, declared.Value);
        return FactPayload.ReadString(final.PayloadJson, FactKeys.Text)?.Length ?? 0;
    }
}
