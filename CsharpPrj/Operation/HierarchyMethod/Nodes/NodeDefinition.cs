using System;
using System.Collections.Generic;
using ParameterResolver.KeyMapping;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 工作流节点 —— Agent 的基础存储单元（逻辑 + 上下文/输出的统一载体）。
    /// 一切逻辑以节点为主：节点既是执行单元（<see cref="FuncName"/>），又是数据容器
    /// （<see cref="ParameterKeys"/> 消费上游/全局上下文，<see cref="ResultKeys"/> 输出
    /// 以节点端口持久化为可按节点检索的存储条目）。入出参可携带节点级 KeyMapping 自由组合。
    /// </summary>
    public sealed class NodeDefinition
    {
        /// <summary>节点唯一键（默认取 FuncName；WorkFlowMethod 侧对应 basicConfig.Callkey）。</summary>
        public string NodeKey { get; init; } = string.Empty;

        /// <summary>所属工作流 ID（存储键命名空间，默认 agent-round）。</summary>
        public string WorkflowId { get; init; } = NodeKeys.DefaultWorkflowId;

        /// <summary>WorkFlowMethod 侧调用键（INodeConfigProvider.CallKey 语义）。</summary>
        public string? CallKey { get; init; }

        /// <summary>节点符号（WorkFlowMethod NodeConfig.Symbol 语义）。</summary>
        public string Symbol { get; init; } = string.Empty;

        /// <summary>注册的工作流函数名（经 WorkFlowMethodUsage 注册，由 WorkFlowMethodUsage.CallFunctionAsync 调用）。</summary>
        public string FuncName { get; init; } = string.Empty;

        /// <summary>输入端口：节点消费哪些上下文键。</summary>
        public IReadOnlyList<string> ParameterKeys { get; init; } = Array.Empty<string>();

        /// <summary>输出端口：节点产出的结果键（持久化为节点输出端口）。</summary>
        public IReadOnlyList<string> ResultKeys { get; init; } = Array.Empty<string>();

        /// <summary>节点级入参键映射（KeyMapping 组合上下文，源可指向其他节点输出端口）。</summary>
        public KeyMappingConfig? InputMapping { get; init; }

        /// <summary>节点级出参键映射（KeyMapping 组合逻辑/下游端口）。</summary>
        public KeyMappingConfig? OutputMapping { get; init; }
    }
}
