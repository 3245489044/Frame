#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using GraphFramework.Models;
using Operation.HierarchyMethod.Nodes.Workflows;
using WorkFlowMethod.Skeletons.NodeProcess.Models;

namespace Operation.HierarchyMethod.Nodes.Relations
{
    /// <summary>
    /// 把工作流骨架 <see cref="RoundSpec"/>（步骤声明）映射为 NodeProcess 的 <see cref="NodeConfig"/> 链。
    /// 只做字段形态转换（RoundStep ↔ NodeConfig），不改变任何领域语义——守卫/ReturnSpec/规则匹配等仍由应用层
    /// handler / StepExec 承担。产出既可作内存 <see cref="Chain{TKey,TNode}"/> 消费，也可经 <see cref="ToJson"/>
    /// 交给 <c>NodeChainService.CreateChain&lt;NodeConfig&gt;</c> 建链。
    /// <para>自 Agent 侧同类型平移（LLM-free 纯映射：GraphFramework + ParameterResolver + 框架 RoundStep 均框架类型）。</para>
    /// </summary>
    public static class RoundSpecChainMapper
    {
        /// <summary>映射契约表：
        /// FuncName→funcPipelineConfig.FuncName + basicConfig.Callkey；EffectiveNodeId→Id/Symbol/Callkey(链键)；
        /// ParameterKeys/ResultKeys→JSON 字符串；InputMapping/OutputMapping→MapKeyConfig/AddParameters；
        /// 顺序/后继→nextNodeConfig.NextNodeKey=下一 EffectiveNodeId（链尾空）。</summary>
        public static Chain<string, NodeConfig> MapToChain(RoundSpec spec, string? chainId = null)
        {
            ArgumentNullException.ThrowIfNull(spec);
            if (spec.Steps is not { Count: > 0 })
                throw new ArgumentException("RoundSpec.Steps 不能为空", nameof(spec));
            var nodes = new List<NodeConfig>(spec.Steps.Count);
            for (var i = 0; i < spec.Steps.Count; i++)
            {
                var next = i + 1 < spec.Steps.Count ? spec.Steps[i + 1].EffectiveNodeId : string.Empty;
                nodes.Add(MapNode(spec.Steps[i], next));
            }
            return new Chain<string, NodeConfig>(chainId ?? spec.Name, nodes, spec.Name);
        }

        public static NodeConfig MapNode(RoundStep step, string nextNodeKey)
        {
            var p = RoundStepMapping.Project(step);
            return new NodeConfig
            {
                Id = p.NodeKey,
                Symbol = p.NodeKey,
                Label = p.FuncName,
                basicConfig = new NodeConfig.BasicConfig { Callkey = p.NodeKey },
                funcPipelineConfig = new NodeConfig.FuncPipelineConfig
                {
                    FuncName = p.FuncName,
                    ParameterKeys = JsonSerializer.Serialize(p.ParameterKeys),
                    ResultKeys = JsonSerializer.Serialize(p.ResultKeys),
                },
                nextNodeConfig = new NodeConfig.NextNodeConfig
                {
                    HandleNextNodeRun = true,
                    NextNodeKey = nextNodeKey,
                    MapKeyConfig = p.InputMapping is null ? string.Empty : JsonSerializer.Serialize(p.InputMapping),
                },
                addNodeConfig = new NodeConfig.AddNodeConfig
                {
                    AddParameters = p.OutputMapping is null ? string.Empty : JsonSerializer.Serialize(p.OutputMapping),
                },
            };
        }

        /// <summary>产出一份等价 <see cref="NodeConfig"/> 链的 JSON（交给 NodeChainService.CreateChain 建链的集成点）。</summary>
        public static string ToJson(RoundSpec spec) => JsonSerializer.Serialize(MapToChain(spec));
    }
}
