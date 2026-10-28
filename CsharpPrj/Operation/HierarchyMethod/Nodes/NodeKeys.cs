using System;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>节点默认工作流键（命名空间单一权威）。</summary>
    public static class NodeKeys
    {
        /// <summary>默认工作流 ID（ur_nodes 命名空间，跨组件单一语义；权威 = <see cref="NodeKeyPlan.DefaultNamespace"/>）。</summary>
        public const string DefaultWorkflowId = NodeKeyPlan.DefaultNamespace;
    }
}
