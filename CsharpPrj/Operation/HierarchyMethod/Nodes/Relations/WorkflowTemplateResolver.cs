using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Relations;

/// <summary>
/// 工作流模板解析器 —— 成员查找链（对齐 C# 类继承 + override，LLM-free）：
/// 派生工作流复用基类步骤，且以同名 <see cref="RoundStep.EffectiveNodeId"/> 覆写（对齐 override）。
/// 解析顺序 = 派生自有步骤在前，基类未覆写步骤补足；配合 <see cref="WorkflowCompiler"/> 校验继承环/类型契约、
/// <see cref="WorkflowResultDeriver"/> 在合并步骤上做分支图出度 0 链尾推导。
/// </summary>
public static class WorkflowTemplateResolver
{
    /// <summary>
    /// 成员查找链合并：派生自有步骤（含覆写）在前，基类未被覆写的步骤补足（对齐 C# 成员查找：实例成员遮蔽基类同名）。
    /// 覆写判定 = EffectiveNodeId 相同（稳定节点实例 ID）。
    /// </summary>
    public static IReadOnlyList<RoundStep> MergeSteps(RoundSpec? derived, RoundSpec? baseSpec)
    {
        if (baseSpec is null || baseSpec.Steps is not { Count: > 0 })
            return derived?.Steps ?? Array.Empty<RoundStep>();
        if (derived is null || derived.Steps is not { Count: > 0 })
            return baseSpec.Steps;

        var derivedIds = new HashSet<string>(derived.Steps.Select(s => s.EffectiveNodeId), StringComparer.Ordinal);
        var merged = new List<RoundStep>(derived.Steps);
        foreach (var bs in baseSpec.Steps)
            if (!derivedIds.Contains(bs.EffectiveNodeId))
                merged.Add(bs);
        return merged;
    }
}