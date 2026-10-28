namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点入史标记位（**中位标记位**，标签可共存）—— 仅声明「有哪些位」，判定规则由宿主策略端口
/// <see cref="IContainerMarkPolicy"/> 提供（框架不持终答/端口语义）。
/// <para><see cref="Terminal"/>：终答节点；<see cref="Numbered"/>：显式声明的中间节点；
/// <see cref="Port"/>：容器↔成员连接端口节点；<see cref="None"/>：仅留全量层（不入史）。</para>
/// </summary>
[Flags]
public enum ContainerMark
{
    /// <summary>不入史（仅任务面板全量层）。</summary>
    None = 0,

    /// <summary>编号中间节点（显式声明；入史供分层气泡）。</summary>
    Numbered = 1,

    /// <summary>终答节点（默认标记；入史为对话真源）。</summary>
    Terminal = 2,

    /// <summary>容器↔成员连接端口节点（可与其他标签共存）。</summary>
    Port = 4,
}

