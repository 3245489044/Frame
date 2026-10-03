using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Relations
{
    /// <summary>
    /// 工作流控制流引用静态化器（LLM-free）—— 把声明式 <see cref="RoundSpec"/> 的「组合 + 控制流」结构静态化为
    /// **可维护的引用关系**（Owns：工作流包含步骤；Calls：上一步骤调用下一步骤，依主路径线性序），
    /// 经引用守卫 <see cref="IReferenceMaintenance.WriteStaticReferencesAsync"/> 幂等写入引用图。
    /// 模板粒度：一次静态化即常驻（配合 WorkflowRunner 的每进程每工作流一次守卫），供图算法/影响分析消费。
    /// 随引用关系推导收敛迁入框架（Nodes.Relations，SingleImplementation / TH-12）；编排只编排，写边委托引用守卫。
    /// </summary>
    public static class WorkflowReferenceStaticizer
    {
        /// <summary>推导工作流控制流/组合静态引用边（纯函数，可单测；无守卫/空 spec 时为空集）。</summary>
        public static IReadOnlyList<StaticReference> Derive(RoundSpec? spec) => DeriveParts(spec).Edges;

        /// <summary>
        /// 提取工作流「稳定步骤键 + 控制流边」的共享骨架（单一提取点，消除 WorkflowResultDeriver / WorkflowCompiler
        /// 各自 Select(EffectiveNodeId) + DeriveWorkflowControlFlowEdges 的重复）。
        /// 控制流边以稳定 NodeId 标识步骤（Same FuncName 多步骤不碰撞），与 WorkflowResultDeriver 链尾推导同源。
        /// 空 spec / 无步骤 → 空键集 + 空边集。
        /// </summary>
        public static (IReadOnlyList<string> Keys, IReadOnlyList<StaticReference> Edges) DeriveParts(RoundSpec? spec)
        {
            if (spec is null || spec.Steps is not { Count: > 0 }) return (Array.Empty<string>(), Array.Empty<StaticReference>());
            var keys = spec.Steps.Select(s => s.EffectiveNodeId).ToList();
            return (keys, NodeRelation.DeriveWorkflowControlFlowEdges(spec.Name, keys));
        }

        /// <summary>经引用守卫把工作流控制流边静态化到引用图（幂等；未装配守卫 → no-op，P5 可空守卫）。</summary>
        public static Task StaticizeAsync(IReferenceMaintenance? grounding, RoundSpec? spec, CancellationToken ct = default)
        {
            if (grounding is null || spec is null) return Task.CompletedTask;
            var refs = Derive(spec);
            return refs.Count == 0 ? Task.CompletedTask : grounding.WriteStaticReferencesAsync(spec.Name, refs, ct);
        }
    }
}
