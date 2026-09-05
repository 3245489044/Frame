using System;
using System.Collections.Generic;
using System.Linq;
using Operation.LoopFoldMethod.Reference.Model;

namespace Operation.LoopFoldMethod.Reference.Derivation;

/// <summary>
/// 树头投影器（Reference 空间维度，纯链尾推导、LLM-free）—— 收敛自 <c>HierarchyMethod.Nodes.Relations.WorkflowResultDeriver</c>：
/// 终答节点 = 控制流引用链（Calls 边）的**链尾**（出度 0，不作为任何 Calls 的 source 的步骤）。
/// 线性链模板下链尾即最后一步；将来控制流演化为图（分支/合并）时，出度 0 推导仍正确。
/// <para>
/// 中立输入：<paramref name="stepNodeIds"/> 为已解析的步骤节点资源 Id 列表（顺序与步骤一致），
/// <paramref name="edges"/> 为控制流边（<see cref="ReferenceEdge"/>）。返回**步骤索引**（映射回原步骤稳定节点）。
/// </para>
/// </summary>
public static class TreeHeadProjector
{
    /// <summary>从控制流引用链（Calls 边）推导终答步骤索引（链尾 = 出度 0；空/退化图兜底最后一步）。</summary>
    public static int ResolveFinalStepIndex(IReadOnlyList<string> stepNodeIds, IReadOnlyList<ReferenceEdge> edges)
    {
        if (stepNodeIds is not { Count: > 0 }) return -1;
        var sources = new HashSet<string>(
            edges.Where(e => string.Equals(e.RefType, ReferenceTypes.Calls, StringComparison.Ordinal))
                .Select(e => e.SourceId),
            StringComparer.Ordinal);
        for (var i = stepNodeIds.Count - 1; i >= 0; i--)
            if (!sources.Contains(stepNodeIds[i]))
                return i;
        return stepNodeIds.Count - 1; // 退化兜底（异常图/全环）
    }
}
