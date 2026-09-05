namespace Operation.LoopFoldMethod.Reference.Model;

/// <summary>
/// 节点输出作用域可见性（Reference 中立形态，收敛自 <c>HierarchyMethod.Nodes.OutputVisibility</c>）：
/// <see cref="WorkflowLocal"/> 仅本工作流可见；<see cref="Exported"/> 跨工作流可见（对齐 public）。
/// 供 <see cref="Derivation.ScopeResolver"/> 纯推演消费；与 HierarchyMethod 的 <c>OutputVisibility</c> 经薄适配互转（见 Gateways/消费方）。
/// </summary>
public enum ReferenceVisibility
{
    /// <summary>工作流局部：仅同工作流可见（局部变量语义）。</summary>
    WorkflowLocal,

    /// <summary>导出：跨工作流可见（public，须显式 exports/Exports 边）。</summary>
    Exported,
}
