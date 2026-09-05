using System;
using System.Collections.Generic;

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
        string WorkflowId,
        IReadOnlyDictionary<string, object?> OutputPorts,
        IReadOnlyDictionary<string, (string Node, string Port)> Provenance,
        string? Error = null,
        string? ErrorClass = null,
        string Backend = "",
        long LatencyMs = 0,
        DateTimeOffset AtUtc = default,
        bool IsFinal = false)
    {
        /// <summary>节点级成败（单一判定点：无 Error 即成功；ErrorClass 为强类型归因）。</summary>
        public bool Ok => Error == null;
    }
}
