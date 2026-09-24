#region USINGS
using System;
using System.Collections.Generic;
using Share.StateManagement;
#endregion

namespace WorkFlowMethod.Execution.Session
{
    public enum NodeStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Skipped
    }

    /// <summary>
    /// 节点级执行状态（Pending→Running→Completed/Failed/Skipped）。
    /// 状态追踪逻辑泛化为 <see cref="TaskExecutionState{TKey,TStatus}"/>（Share.StateManagement，TH-12 单一实现），
    /// 本类型注入节点状态枚举 <see cref="NodeStatus"/> 并保留既有 API（Status/NodeKey/ErrorMessage/StartedAt/CompletedAt/ExecutionCount）。
    /// 运行态受控词与转移表的唯一来源 = <c>Operation.HierarchyMethod.Hierarchy.Dataflow.RunState</c>
    /// （宿主阶段词经该处 <c>RunStateStage.FromStatusText</c> 映射；本类不再自带第二套词表与运行态投影入口）。
    /// </summary>
    public class NodeExecutionState : TaskExecutionState<string, NodeStatus>
    {
        /// <summary>节点标识（兼容既有读取；底层即 <see cref="TaskExecutionState{TKey,TStatus}.Key"/>）。</summary>
        public string NodeKey => Key;

        public NodeExecutionState(string nodeKey)
            : base(nodeKey)
        {
        }

        /// <summary>开始执行：仅 Pending / Failed → Running。</summary>
        public void Start()
            => TryTransitionTo(NodeStatus.Running,
                s => s == NodeStatus.Pending || s == NodeStatus.Failed,
                markStarted: true);

        /// <summary>标记完成：仅 Running → Completed。</summary>
        public void Complete()
            => TryTransitionTo(NodeStatus.Completed,
                s => s == NodeStatus.Running,
                markCompleted: true);

        /// <summary>标记失败：仅 Running → Failed。</summary>
        public void Fail(string errorMessage)
            => TryTransitionTo(NodeStatus.Failed,
                s => s == NodeStatus.Running,
                markCompleted: true,
                error: errorMessage ?? "未知错误");

        /// <summary>跳过：仅 Pending → Skipped。</summary>
        public void Skip(string? reason = null)
            => TryTransitionTo(NodeStatus.Skipped,
                s => s == NodeStatus.Pending,
                markCompleted: true,
                error: reason);

        public override string ToString()
        {
            return $"NodeState [Key={Key}, Status={Status}, Count={ExecutionCount}]";
        }
    }
}
