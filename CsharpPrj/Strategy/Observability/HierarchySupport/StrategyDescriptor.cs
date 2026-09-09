namespace Share.HierarchySupport
{
    /// <summary>
    /// 策略描述符 —— 把「策略」从裸字符串 ID 升级为携带行为载荷的结构化声明。
    /// 当前仅注册式承载（strategy.current / strategy.available 契约），不强制引擎执行（策略完全实施由
    /// <see cref="ITransitionGuard"/> 校验目标 ∈ 可用集）。行为差异（执行器提示 / 提示词策略 / 预算阈值）
    /// 经本描述符透传，供 executor-resolver（<see cref="IAgentExecutor"/> 按 Name 匹配）与 provide_prompt 消费。
    /// </summary>
    public sealed record StrategyDescriptor(
        string Id,
        string? Description = null,
        string? ExecutorHint = null,
        string? PromptPolicy = null,
        int? ThresholdDelta = null,
        string? BaseUrl = null,
        string? ClientType = null)
    {
        public override string ToString() => Id;
    }
}
