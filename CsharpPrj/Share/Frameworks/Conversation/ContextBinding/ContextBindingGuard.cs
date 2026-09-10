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
}
