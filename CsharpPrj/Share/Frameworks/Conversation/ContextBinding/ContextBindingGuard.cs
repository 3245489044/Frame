using System;
using System.Collections.Generic;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 上下文绑定门禁（P4）—— 封装 R1–R6 校验 + 可选 strict fail-fast + 诊断 sink，供
/// ① SendAsync 后 / ② RenderViewAsync 时 / ③ 单测 三类触发点统一调用。
/// strict = true 时任一违规即抛 <see cref="InvalidOperationException"/>（strictConfig 门禁 fail-fast）；
/// strict = false 时仅经 <paramref name="sink"/> 写诊断（差异报告 / __context_binding），不阻断（零回归）。
/// </summary>
public static class ContextBindingGuard
{
    public static IReadOnlyList<BindingViolation> Check(
        string scope,
        IReadOnlyList<ConversationMessage>? uiView,
        IReadOnlyList<ConversationMessage>? llmView,
        PromptSnapshot? snapshot,
        bool strict,
        Action<BindingViolation>? sink = null)
    {
        var violations = ContextBindingValidator.Validate(uiView, llmView, snapshot);
        if (sink is not null)
            foreach (var v in violations)
                sink(v);
        if (strict && violations.Count > 0)
            throw new InvalidOperationException(
                $"[ContextBinding] UI ≡ LLM 违规({scope}): {string.Join(" | ", violations)}");
        return violations;
    }

    /// <summary>
    /// 校验并**一次聚合**报告（诊断爆发抑制）：<see cref="IBindingDiagnosticSink.Warn"/> 每调用一条，
    /// 明细走 <see cref="IBindingDiagnosticSink.Debug"/>；返回同一报告供调用方断言/取证。
    /// </summary>
    /// <param name="scope">校验作用域。</param>
    /// <param name="uiView">UI 视图消息（可空）。</param>
    /// <param name="llmView">LLM 输入消息（可空）。</param>
    /// <param name="snapshot">写侧快照（可空）。</param>
    /// <param name="strict">true ⇒ 任一违规 fail-fast。</param>
    /// <param name="sink">诊断出口（可空 ⇒ 只返回报告）。</param>
    public static BindingReport CheckReported(
        string scope,
        IReadOnlyList<ConversationMessage>? uiView,
        IReadOnlyList<ConversationMessage>? llmView,
        PromptSnapshot? snapshot,
        bool strict,
        IBindingDiagnosticSink? sink = null)
    {
        var violations = ContextBindingValidator.Validate(uiView, llmView, snapshot);
        var report = BindingReport.From(scope, violations);
        if (sink is not null)
        {
            sink.Warn(report);
            foreach (var violation in violations)
                sink.Debug(violation);
        }

        if (strict && violations.Count > 0)
            throw new InvalidOperationException(
                $"[ContextBinding] UI ≡ LLM 违规({scope}): {string.Join(" | ", violations)}");
        return report;
    }
}
