using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.Workflows
{
    /// <summary>
    /// 节点执行快照构造<b>唯一工厂</b>（A5 收敛，消 D4 三处平行构造）。
    /// <para>
    /// 收敛对象（实测三处，全在 Agent 侧）：<c>RoundStepInvoker</c>（执行侧产物）、
    /// <c>SignalWorkflowRunner.BuildNodes</c>（spec + 折叠 Outputs）、<c>FoldConversationRunner.BuildNodes</c>
    /// （spec + 折叠顶点）。三处此前各自内联 <c>new NodeExecutionRecord</c> ⇒ 字段口径（NodeKey/WorkflowId/
    /// 端口/Provenance/AtUtc）在三个写点各自演进，无人声明谁是权威（SingleImplementation / TH-12）。
    /// </para>
    /// <para>
    /// 纪律：<b>本工厂是生产侧唯一 <c>new NodeExecutionRecord</c> 点</b>；WorkflowId 一律取
    /// <see cref="NodeKeys.DefaultWorkflowId"/>（存储命名空间）—— 禁填 <c>spec.Name</c>（来源名入图端点 Id 会分裂节点端点）。
    /// 无 IO、无键构造、无门禁交互（回退 = 各调用点内联构造）。
    /// </para>
    /// <para>注：<c>Share.StateManagement.NodeExecutionRecord</c> 为同名异类型（LoopStage 通用循环簿记），不属本工厂收敛面。</para>
    /// </summary>
    public static class NodeExecutionRecordFactory
    {
        /// <summary>空入参溯源（无上游端口依赖的构造路径）。</summary>
        private static readonly IReadOnlyDictionary<string, (string Node, string Port)> EmptyProvenance =
            new Dictionary<string, (string Node, string Port)>();

        /// <summary>生产侧唯一构造点（其余方法一律经此；禁在别处 <c>new NodeExecutionRecord</c>）。</summary>
        private static NodeExecutionRecord Create(
            string nodeKey,
            string workflowId,
            IReadOnlyDictionary<string, object?> outputPorts,
            IReadOnlyDictionary<string, (string Node, string Port)> provenance,
            DateTimeOffset atUtc)
            => new(nodeKey, workflowId, outputPorts, provenance, AtUtc: atUtc);

        /// <summary>
        /// 执行侧构造（<c>RoundStepInvoker</c> 语义）：节点已执行完，端口/入参溯源来自执行产物。
        /// <paramref name="atUtc"/> 由调用方给（执行完成时刻），工厂不读时钟（保持纯函数、对拍可比）。
        /// </summary>
        public static NodeExecutionRecord FromExecution(
            string nodeKey,
            string workflowId,
            IReadOnlyDictionary<string, object?> outputPorts,
            IReadOnlyDictionary<string, (string Node, string Port)> provenance,
            DateTimeOffset atUtc)
        {
            ArgumentNullException.ThrowIfNull(outputPorts);
            ArgumentNullException.ThrowIfNull(provenance);
            return Create(nodeKey, workflowId, outputPorts, provenance, atUtc);
        }

        /// <summary>
        /// 声明侧构造（折叠链路语义）：一个 <see cref="RoundStep"/> + 其首个结果键的产出值 ⇒ 一个节点。
        /// WorkflowId 固定取存储命名空间（禁来源名）。
        /// </summary>
        public static NodeExecutionRecord FromStepPort(
            RoundStep step,
            string portKey,
            object? value,
            DateTimeOffset atUtc = default)
        {
            ArgumentNullException.ThrowIfNull(step);
            return Create(
                step.EffectiveNodeId,
                NodeKeys.DefaultWorkflowId,
                new Dictionary<string, object?> { [portKey] = value },
                EmptyProvenance,
                atUtc);
        }

        /// <summary>
        /// 声明侧整批构造（两 runner 共用形）：按 <see cref="RoundSpec.Steps"/> 顺序，逐步骤取「首个结果键」的产出值，
        /// 可提取到值者建节点（无产出/空白**不造节点**，不伪造产物）。
        /// <para>跳过规则（与两 runner 既有行为等价）：无 <see cref="RoundStep.ResultKeys"/> → 跳过；
        /// <see cref="RoundStep.EffectiveNodeId"/> 空白 → 跳过；取值为 <c>null</c> → 跳过；取值为空白字符串 → 跳过。</para>
        /// </summary>
        /// <param name="spec">本轮骨架（提供步骤序与端口声明）。</param>
        /// <param name="portValueOf">取某步骤的首个结果键产出值（返回 <c>null</c> 即该步无产出）。</param>
        /// <param name="atUtc">产出时刻（缺省 <c>default</c>，与既有两 runner 一致，保持对拍等价）。</param>
        public static IReadOnlyList<NodeExecutionRecord> FromStepPorts(
            RoundSpec spec,
            Func<RoundStep, object?> portValueOf,
            DateTimeOffset atUtc = default)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(portValueOf);

            var records = new List<NodeExecutionRecord>();
            foreach (var step in spec.Steps)
            {
                if (step.ResultKeys is not { Count: > 0 }) continue;
                if (string.IsNullOrWhiteSpace(step.EffectiveNodeId)) continue;

                var value = portValueOf(step);
                if (value is null) continue;
                if (value is string text && string.IsNullOrWhiteSpace(text)) continue;

                records.Add(FromStepPort(step, step.ResultKeys[0], value, atUtc));
            }

            return records;
        }
    }
}
