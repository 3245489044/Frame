using System;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 入史标记策略端口（**规则由宿主提供**；框架只声明标记位 <see cref="ContainerMark"/> 与契约）。
/// 实现方（Agent）决定「终答/端口/编号」等产品语义，框架不持领域词。
/// </summary>
public interface IContainerMarkPolicy
{
    /// <summary>解析某节点的入史标记。</summary>
    /// <param name="spec">本轮骨架（声明面来源；null = 仅默认规则）。</param>
    /// <param name="record">待判节点产出快照。</param>
    /// <param name="terminalNodeKey">终答来源节点键。</param>
    ContainerMark Resolve(Operation.HierarchyMethod.Nodes.Workflows.RoundSpec? spec, NodeExecutionRecord record, string? terminalNodeKey);
}
