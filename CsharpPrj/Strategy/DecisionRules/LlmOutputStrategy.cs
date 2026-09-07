namespace Strategy.DecisionRules
{
    /// <summary>
    /// LLM 延迟请求描述（引擎同步产出，LLM-free）：把「规则声明要走 LLM 兜底」固化为一份请求快照。
    /// 核心不执行、不解析 LLM（保持 LLM-free 红线）；异步执行 + 结果解析由消费方薄包装适配层完成——
    /// 适配层看到 <see cref="DecisionRuleResult.Kind"/>==<see cref="OutputKind.LLM"/> 且 <see cref="LlmRequest"/> 非空时，
    /// 按 <see cref="ParseJson"/> 选择 JSON/纯文本生成器调用 LLM，再行解析。
    /// </summary>
    public sealed record LlmCallDescriptor(string System, string User, bool ParseJson);

    /// <summary>
    /// LLM 输出策略（LLM-free）：经占位符渲染器把 <c>llmSystemTemplate</c> / <c>llmUserTemplate</c>（缺省回退 <c>template</c>）
    /// 渲染为最终提示快照，产出 <see cref="LlmCallDescriptor"/>。无状态、可单例复用；默认随 <see cref="DefaultOutputStrategyRegistry"/> 注册，
    /// 使规则显式声明 <c>then.type:"llm"</c> 时引擎确定性产出描述（异步执行与否由消费方决定，即「默认关」语义）。
    /// </summary>
    public sealed class LlmOutputStrategy : IOutputStrategy
    {
        /// <summary>无状态单例。</summary>
        public static LlmOutputStrategy Default { get; } = new();

        public OutputKind Kind => OutputKind.LLM;

        public object? Render(OutputClause clause, DecisionRuleContext ctx, ITemplateRenderer renderer)
        {
            var system = renderer.Render(clause.LlmSystemTemplate, ctx);
            var user = renderer.Render(string.IsNullOrEmpty(clause.LlmUserTemplate) ? clause.Template : clause.LlmUserTemplate, ctx);
            return new LlmCallDescriptor(system ?? "", user ?? "", clause.LlmParseJson);
        }
    }
}
