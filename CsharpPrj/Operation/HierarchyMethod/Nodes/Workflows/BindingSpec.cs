using System;
using System.Collections.Generic;
using ParameterResolver.KeyMapping;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Nodes.Workflows
{
    /// <summary>
    /// 工作流容器的绑定契约（端口 + 全局映射）。静态骨架 RoundSpec 与动态片段
    /// 均可构造本 DTO，供共享 <c>DataContainerBinder</c> 消费——解耦静态/动态两条路径的绑定入口。
    /// 纯 LLM-free DTO（从 Agent.Contract.DataContainers 下沉，SingleImplementation / TH-12）。
    /// </summary>
    public sealed class BindingContainer
    {
        public IReadOnlyList<PortSpec>? InputPorts { get; init; }
        public IReadOnlyList<PortSpec>? OutputPorts { get; init; }
        public KeyMappingConfig? GlobalKeyMapping { get; init; }
    }

    /// <summary>单个节点的绑定契约（参数键 / 结果键 / 入出参映射）。RoundStep 与片段节点均可构造本 DTO。</summary>
    public sealed class BindingStep
    {
        public IReadOnlyList<string> ParameterKeys { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ResultKeys { get; init; } = Array.Empty<string>();
        public KeyMappingConfig? InputMapping { get; init; }
        public KeyMappingConfig? OutputMapping { get; init; }
    }
}
