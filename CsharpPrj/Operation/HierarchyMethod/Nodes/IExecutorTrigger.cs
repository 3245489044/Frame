namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 执行触发回调端口（<c>NodeExecutionService</c> 剥离出的 LLM 触发段）—— 纯节点执行层在「该节点执行」时回调，
    /// 由 Agent 编排决定是否触发 LLM 子智能体。框架本体**零 LLM**（P1）：只声明回调契约，不承载任何 LLM 知识。
    /// 未装配 → false（纯确定性执行，零 LLM，不消耗 AI 额度，P3/P5）。
    /// </summary>
    public interface IExecutorTrigger
    {
        /// <summary>节点执行触发决策：true = 交由 Agent 触发 LLM 子智能体路径；false = 纯确定性执行。</summary>
        bool ShouldTriggerNode(NodeDefinition node);
    }
}
