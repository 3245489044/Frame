namespace Share.HierarchySupport
{
    /// <summary>
    /// 任务级调度导演模式 —— 决定本轮整体状态变更由谁提议（<see cref="IStateDirector"/>）：
    /// <para><b>Hybrid</b> = <see cref="Agent.Axioms.Scheduling.LlmStateDirector"/>（LLM 提议自由探索 + 计算守卫）；默认，保持现状。</para>
    /// <para><b>Config</b> = <see cref="Agent.Axioms.Scheduling.ConfigStateDirector"/>（ur_config 规则集表驱动，确定性、零 LLM）。</para>
    /// <para><b>Deterministic</b> = <see cref="Agent.Axioms.Scheduling.NoopStateDirector"/>（纯规则迭代：无 LLM 导演、无 ur_config 依赖；
    /// 迭代仅由 rule_match 规则 + 预算驱动）。</para>
    /// <para><b>Semantic</b> = <see cref="Agent.Axioms.Scheduling.SemanticVerdictDirector"/>（Routing 语义矩阵导演：读遥测 → DecisionRoundFacade
    /// 裁决 → SchedulerActionProjection，只产 Continue/SwitchStrategy；终值/无命中 → null 回终止面。intent/produced 在调度上下文
    /// 未到源时置导演降级(director_state=degraded)，属 Agent.Operation.Routing 单一语义面收口）。</para>
    /// 由任务池任务（<c>AgentTaskItem.SchedulingMode</c>）选择，经 Functor 透传到 <c>AgentRunner</c> 构造调度状态机。
    /// </summary>
    public enum SchedulingMode
    {
        /// <summary>LLM 导演（自由探索源）。</summary>
        Hybrid,

        /// <summary>配置式导演（ur_config 规则表驱动，零 LLM）。</summary>
        Config,

        /// <summary>纯规则迭代：无 LLM 导演、无 ur_config 依赖；迭代仅由 rule_match 规则 + 预算驱动（非自主迭代源）。</summary>
        Deterministic,

        /// <summary>Routing 语义矩阵导演（零 LLM 确定性；读遥测经 DecisionRoundFacade 裁决，观测/策略域可判定分支）。</summary>
        Semantic,
    }
}
