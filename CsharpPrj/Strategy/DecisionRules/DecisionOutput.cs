namespace Strategy.DecisionRules
{
    /// <summary>
    /// JSON 聚拢输出信封（全决策点唯一输出契约；领域中立，LLM-free）。由求值核心的 <see cref="DecisionRuleResult"/>
    /// 适配而来，供薄包装层（Agent/任意 JSON 消费者）统一消费后映到各自契约（StateTransitionProposal / AxiomActionResult /
    /// EvalOutcome / LlmResult 等）。schema：
    /// <code>{ matched, ruleId, kind, fallback, result }</code>，
    /// 其中 kind 为 <see cref="OutputKind"/> 的 JSON 串（text/json/tool_call/…/llm），result 为单一 JSON/文本/工具调用/LLM 请求描述。
    /// </summary>
    public sealed record DecisionOutput(
        bool Matched,
        string? RuleId,
        OutputKind Kind,
        bool Fallback,
        object? Result)
    {
        /// <summary>把引擎 <see cref="DecisionRuleResult"/> 归一为聚拢信封（单点映射；消费方不再读引擎散字段）。</summary>
        public static DecisionOutput FromResult(DecisionRuleResult r)
            => new(
                Matched: r.Matched,
                RuleId: r.MatchedRule?.Id,
                Kind: r.Kind,
                Fallback: r.IsFallback,
                Result: Collapse(r));

        private static object? Collapse(DecisionRuleResult r)
        {
            switch (r.Kind)
            {
                case OutputKind.Text:
                case OutputKind.Json:
                    return r.Text;
                case OutputKind.ToolCall:
                    return r.ToolCalls is { Count: 1 } ? r.ToolCalls[0] : r.ToolCalls;
                case OutputKind.ToolCalls:
                    return r.ToolCalls;
                case OutputKind.Structured:
                case OutputKind.Merge:
                case OutputKind.Delegate:
                    return r.Structured is not null ? r.Structured : r.DelegateName;
                case OutputKind.LLM:
                    return r.LlmRequest;
                default:
                    return r.Text;
            }
        }
    }
}
