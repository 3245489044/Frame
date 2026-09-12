#nullable enable

using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes.Workflows;
using ParameterResolver.KeyMapping;

namespace Operation.HierarchyMethod.Nodes.Relations
{
    /// <summary>
    /// <see cref="RoundStep"/> → 节点字段投影单一来源（D7）：运行期 <see cref="NodeDefinition"/> 与声明期
    /// <c>NodeConfig</c> 两处映射共用同一字段口径，避免 NodeKey/Id、CallKey/Callkey、端口键等在两处各自演进漂移。
    /// 仅做字段投影，不合并两目标类型（JSON 串化等目标专属适配留在各调用方）。
    /// </summary>
    public static class RoundStepMapping
    {
        public static (string NodeKey, string FuncName, IReadOnlyList<string> ParameterKeys,
            IReadOnlyList<string> ResultKeys, KeyMappingConfig? InputMapping, KeyMappingConfig? OutputMapping)
            Project(RoundStep step)
        {
            ArgumentNullException.ThrowIfNull(step);
            return (step.EffectiveNodeId, step.FuncName, step.ParameterKeys, step.ResultKeys,
                step.InputMapping, step.OutputMapping);
        }
    }
}
