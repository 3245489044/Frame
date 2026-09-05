using System;
using System.Collections.Generic;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes.Workflows;
using Operation.LoopFoldMethod.Reference.Derivation;
using Operation.LoopFoldMethod.Reference.Model;

namespace Operation.HierarchyMethod.Nodes.Relations
{
    /// <summary>
    /// 工作流结果推导器（引用域侧：纯链尾推导，LLM-free）—— 基于「引用关系」推导终答步骤（Node 即 Result，SingleImplementation / TH-12）。
    /// 终答节点 = 控制流引用链（Calls 边）的**链尾**（出度 0，不作为任何 Calls 的 source 的步骤），
    /// 复用 <see cref="NodeRelation.DeriveWorkflowControlFlowEdges(string, IReadOnlyList{string})"/> —— 与
    /// <see cref="WorkflowReferenceStaticizer"/> 静态化落 LiteDB 的引用关系**同源**，不另起
    /// FinalNode/FinalPort 字符串契约。线性链模板下链尾即最后一步；将来控制流演化为图（分支/合并）时，出度 0 推导仍正确。
    /// 应用侧取终答文本（<c>ResolveFinalAnswer</c>）依赖执行记录/日志，留于 Agent 应用层。
    /// 本类型由 Agent.Reference.Relations 迁入框架（Nodes.Relations），双源收敛为框架单一权威。
    /// </summary>
    public static class WorkflowResultDeriver
    {
        /// <summary>
        /// 从控制流引用链（Calls 边）推导终答步骤索引。
        /// 链尾 = 其 NodeId 不作为任何 Calls 的 source（出度 0）；纯线性链下 = 最后一步。
        /// 注意：节点资源 Id 用 NodeId 命名（<see cref="NodeRelation.NodeId(string,string)"/>），同一 FuncName 多步骤
        /// （如 collect-analyze-act 的 cline_execute×2）经 <see cref="RoundStep.EffectiveNodeId"/> 区分（collect/act），
        /// 同名函数不再碰撞；此处返回**步骤索引**定位到稳定节点。
        /// 链尾推导唯一权威收敛至 <see cref="TreeHeadProjector"/>（Reference 中立层），本方法仅做
        /// RoundSpec → 中立输入（全节点资源 Id + 中立引用边）桥接，不复制推导逻辑（SingleImplementation / TH-12）。
        /// </summary>
        public static int ResolveFinalStepIndex(RoundSpec spec)
        {
            if (spec is null || spec.Steps is not { Count: > 0 }) return -1;
            var (keys, edges) = WorkflowReferenceStaticizer.DeriveParts(spec);
            var wf = spec.Name;
            var stepNodeIds = keys.Select(k => NodeRelation.NodeId(wf, k)).ToList();
            var refEdges = edges.Select(e => new ReferenceEdge(e.RefType, e.SourceId, e.TargetId)).ToList();
            return TreeHeadProjector.ResolveFinalStepIndex(stepNodeIds, refEdges);
        }
    }
}
