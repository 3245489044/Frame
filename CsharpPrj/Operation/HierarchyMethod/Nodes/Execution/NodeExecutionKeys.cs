namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 中立节点编排键名约定（与 Agent 侧 RoundKeys 对齐）：编排器据此从数据空间读取决策/执行结果，不硬编码 Agent 类型。
/// 框架只定义键命名约定，具体语义由 Agent 投影器/策略按需解释。
/// </summary>
public static class NodeExecutionKeys
{
    /// <summary>决策词键（rule_match 产出，Agent RoundKeys.Decision）。</summary>
    public const string Decision = "decision";
}
