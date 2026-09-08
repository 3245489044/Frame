using System;
using Operation.LoopFoldMethod.Reference.Derivation;
using Operation.LoopFoldMethod.Reference.Model;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Nodes;

/// <summary>
/// 节点输出作用域判定（写侧，对齐 C# 块级作用域 + public 成员）—— 纯函数，LLM-free。
/// <para>
/// 判定逻辑唯一权威收敛至 <see cref="ScopeResolver"/>（Reference 中立层，SingleImplementation / TH-12），
/// 本类型仅把写侧持久化形态 <see cref="OutputVisibility"/> 翻译为中立 <see cref="ReferenceVisibility"/> 后薄委托，
/// <b>不复制比较逻辑</b>。同工作流恒可见、跨工作流仅 Exported 可见的语义由中立实现单一承载。
/// </para>
/// 供 <see cref="NodeOutputStore"/> 检索时作用域过滤；图层的 exports 边由
/// <c>NodeRelationDeriver.DeriveExportsEdge</c> 声明（对齐类型化契约，边与元数据一致）。
/// </summary>
public static class OutputScopeResolver
{
    /// <summary>判定源工作流产出是否对请求工作流可见（薄委托中立 <see cref="ScopeResolver"/>）。</summary>
    public static bool IsVisibleTo(string sourceWorkflowId, OutputVisibility visibility, string requestingWorkflowId)
        => ScopeResolver.IsVisibleTo(sourceWorkflowId, ToReference(visibility), requestingWorkflowId);

    /// <summary>写侧可见性 → 中立可见性（两者为同义平行枚举，语义一一对应；写侧持久化形态保留）。</summary>
    private static ReferenceVisibility ToReference(OutputVisibility visibility)
        => visibility == OutputVisibility.Exported ? ReferenceVisibility.Exported : ReferenceVisibility.WorkflowLocal;
}
