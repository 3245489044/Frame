namespace Strategy.DecisionRules.Configurable
{
    /// <summary>
    /// 决策依据供给端口（IoC 反转注入点）—— 决策控制框架的"外部动态注入"契约。
    /// 实现方由装配层注入（Agent 等），可取任意外部源（ur_config / 规则文件 / DB），按决策点
    /// 返回<b>当前生效</b>的控制规格；框架每次求值经此动态取依据，实现"决策依据外部配置、动态演算"。
    /// </summary>
    /// <remarks>
    /// 设计约束（对齐 TH-12 / SingleImplementation）：实现只承载【供给数据】；
    /// 【如何从 Spec 构筑/校验/求值】唯一在 <see cref="ConfigDrivenDecisionController"/>，不建第二套求值路径。
    /// 未注册/空返回 null → 门面按 <see cref="DecisionControlSpec.Fallback"/> 兜底 fail-safe，不静默吞。
    /// </remarks>
    public interface IDecisionControlSource
    {
        /// <summary>取指定决策点当前生效的决策控制规格；未注册/空返回 null（门面按兜底 stay/continue）。</summary>
        /// <param name="decisionPoint">决策点键（如 agent.loop.eval）；与 Spec.DecisionPoint 一致性身份。</param>
        DecisionControlSpec? Get(string decisionPoint);
    }

    /// <summary>空供给器（缺失装配的占位；默认构造的兜底，保持 _source 非 null）。</summary>
    internal sealed class NullControlSource : IDecisionControlSource
    {
        public static readonly NullControlSource Instance = new();
        public DecisionControlSpec? Get(string decisionPoint) => null;
    }
}