using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes.Execution;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 节点执行快照 —— 结果的一等载体（Node 即 Result）。
    /// 一次步骤执行 = 一个节点（<see cref="AgentNode"/>）的一次产出，聚合：
    /// 输出端口（<see cref="OutputPorts"/>，即 ResultKeys 各端口结构化值）、
    /// 入参溯源（<see cref="Provenance"/>，参数 ← 上游节点输出端口，与图 NodeToNode 边同源）、
    /// 执行元数据（成败/归因/后端/耗时）。
    /// 该类型是 <c>RoundPipelineContext.RecordStep</c> 暂存的 node/props/input 的聚合视图，
    /// 不引入平行执行模型（SingleImplementation / TH-12）。
    /// </summary>
    public sealed record NodeExecutionRecord(
        string NodeKey,
        /// <summary>
        /// **存储命名空间**（ur_nodes 的 WorkflowId 列 + 图端点 Id 前缀 <c>{workflowId}.{nodeKey}</c>）——
        /// 必须与落库侧 <see cref="NodeStepExecutionService"/> 的 workflowId 一致（默认 = NodeKeys.DefaultWorkflowId）。
        /// 禁填工作流**来源名**（spec.Name）：图端点 Id 含该值，填来源名会使同一逻辑节点分裂成两个端点、
        /// 时序边落到读侧找不到的命名空间。
        /// </summary>
        string WorkflowId,
        IReadOnlyDictionary<string, object?> OutputPorts,
        IReadOnlyDictionary<string, (string Node, string Port)> Provenance,
        string? Error = null,
        string? ErrorClass = null,
        string Backend = "",
        long LatencyMs = 0,
        DateTimeOffset AtUtc = default,
        bool IsFinal = false,
        // 节点实例键（F1）：缺省 = 任务键；同任务并发/重放时 = {taskKey}:{round}:{attempt}。
        // 与 NodeKey 合成存储主键（NodeInstanceRef.ToStorageKey）；null = 旧路径（裸键），仅迁移期兼容。
        string? InstanceKey = null,
        // 实例容器键（通常 = 任务键；与 InstanceKey 分离，供图端点/归属边定位）。
        string? ContainerKey = null,
        // 实际注入键（E2 观测面）：执行器在入参解析/映射完成后上报，供框架在步骤边界完成契约判据。
        // null = 未上报 ⇒ 判据跳过（零回归：任何 INodeExecutor 实现不上报也不会失去既有保护）。
        IReadOnlyCollection<string>? InjectedKeys = null,
        // 步骤级契约结论（M5）：执行边界只上报事实，动作在轮级 Apply 统一裁决。
        RoundContractVerdict? ContractVerdict = null)
    {
        /// <summary>节点级成败（单一判定点：无 Error 即成功；ErrorClass 为强类型归因）。</summary>
        public bool Ok => Error == null;
    }
}
