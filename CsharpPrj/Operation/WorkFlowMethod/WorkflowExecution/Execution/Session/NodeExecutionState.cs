#region USINGS
using System;
using System.Collections.Generic;
using Modules.Node.RunState;
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
    /// 运行态受控词与转移表唯一来源 = <see cref="NodeRunState"/>（宿主阶段词经声明覆盖入口映射，不各写一套词表）。
    /// </summary>
    public class NodeExecutionState : TaskExecutionState<string, NodeStatus>
    {
        /// <summary>宿主阶段词 → 框架受控词的声明覆盖（默认空：全部命中框架内建映射；无命中 ⇒ null，不伪造 Idle）。</summary>
        public static readonly IReadOnlyDictionary<string, RunStateStage> StageOverrides = new Dictionary<string, RunStateStage>();

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

        /// <summary>
        /// 运行态投影：宿主阶段词（<see cref="NodeStatus"/>）→ 框架受控词（<see cref="RunStateStage"/>）。
        /// 无命中 ⇒ null（不伪造 Idle）；覆盖经 <see cref="StageOverrides"/> 声明注入。
        /// </summary>
        public NodeRunState? ToRunState()
            => NodeRunState.FromExternalStage(Status.ToString(), StageOverrides);

        public override string ToString()
        {
            return $"NodeState [Key={Key}, Status={Status}, Count={ExecutionCount}]";
        }
    }
}
