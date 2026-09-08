#region USINGS
using System;
using System.Collections.Generic;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Models
{
    /// <summary>
    /// 节点即存储单元 —— 框架级节点输出模型（去 Agent 依赖的「传递模型」）。
    /// 以 <see cref="NodeKey"/> + <see cref="OutputPorts"/> 组织单个节点的全部输出端口；
    /// S5 由上层（HierarchyMethod）适配器对接其 SQLite 实现（NodeOutputStore/ur_nodes）。
    /// </summary>
    public sealed class NodeOutputResult
    {
        public string NodeKey { get; init; } = string.Empty;

        public string WorkflowId { get; init; } = string.Empty;

        /// <summary>该节点产出的端口值字典（port → value，Ordinal 比较）。</summary>
        public Dictionary<string, object?> OutputPorts { get; } = new(StringComparer.Ordinal);
    }
}