using System;
using Operation.LoopFoldMethod.Reference.Model;

namespace Operation.LoopFoldMethod.Reference.Derivation;

/// <summary>
/// 节点输出作用域判定（Reference 空间维度，纯函数、LLM-free）—— 收敛自 <c>HierarchyMethod.Nodes.OutputScopeResolver</c>，
/// 以中立 <see cref="ReferenceVisibility"/> 表达可见性（语义不动）：
/// <list type="bullet">
///   <item>同工作流访问 → 恒可见（局部变量语义）；</item>
///   <item>跨工作流访问 → 仅 <see cref="ReferenceVisibility.Exported"/>（public，须显式 exports/Exports 边）可见。</item>
/// </list>
/// </summary>
public static class ScopeResolver
{
    /// <summary>判定源工作流产出是否对请求工作流可见。</summary>
    public static bool IsVisibleTo(string sourceWorkflowId, ReferenceVisibility visibility, string requestingWorkflowId)
    {
        if (string.IsNullOrEmpty(requestingWorkflowId) || string.IsNullOrEmpty(sourceWorkflowId))
            return false;
        if (string.Equals(sourceWorkflowId, requestingWorkflowId, StringComparison.Ordinal))
            return true; // 同工作流：局部 + 导出均可见
        return visibility == ReferenceVisibility.Exported; // 跨工作流：仅 public（Exported）
    }
}
