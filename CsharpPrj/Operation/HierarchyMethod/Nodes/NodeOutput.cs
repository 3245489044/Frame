using System;
using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes
{
    /// <summary>
    /// 节点输出可见性（对齐 C# 块级作用域变量 / public 成员）：
    /// <see cref="WorkflowLocal"/> = 局部变量，默认仅工作流内下游可见；
    /// <see cref="Exported"/> = public 成员，跨工作流可见（须显式 exports 边声明）。
    /// </summary>
    public enum OutputVisibility
    {
        WorkflowLocal,

        Exported,
    }

    /// <summary>
    /// 单个节点的持久化输出 —— 节点即存储单元：以 <see cref="NodeKey"/> + <see cref="OutputPorts"/>
    /// 组织该节点产出的全部端口，跨轮/跨任务可按节点检索复用，并经 KeyMapping 供下游节点组合。
    /// 节点信号元数据（<see cref="Module"/> / <see cref="Region"/> / <see cref="NodeType"/> /
    /// <see cref="Freshness"/> / <see cref="ValueScore"/> / <see cref="OutputDigest"/> /
    /// <see cref="Sticky"/> / <see cref="Priority"/>）为「节点 + 信号驱动」的最近/价值/区域评判依据，
    /// 与输出端口一起落库、一起读回（信号摘要免读正文）。
    /// </summary>
    public sealed class NodeOutput
    {
        public string NodeKey { get; init; } = string.Empty;

        public string WorkflowId { get; init; } = "agent-round";

        /// <summary>可见性元数据：默认 <see cref="OutputVisibility.WorkflowLocal"/>（局部），
        /// 跨工作流访问须 <see cref="OutputVisibility.Exported"/>（public，显式 exports 边）。</summary>
        public OutputVisibility Visibility { get; init; } = OutputVisibility.WorkflowLocal;

        /// <summary>该节点产出的端口值字典（port → value）。</summary>
        public Dictionary<string, object?> OutputPorts { get; } = new(StringComparer.Ordinal);

        /// <summary>功能模块（execution / task / plan / decide / ...）：节点自动分组依据。可空 = 未标注。</summary>
        public string? Module { get; init; }

        /// <summary>区域（上下文分组单元，通常由 module × taskKey 派生，如 task.proj-x）。可空 = 未派生。</summary>
        public string? Region { get; init; }

        /// <summary>节点类别（execution_node / task_node / ...）。可空 = 未标注。</summary>
        public string? NodeType { get; init; }

        /// <summary>新鲜度（落库时间戳，Unix 秒；缺省 0 = 用落库时序推断最近，零回归）。</summary>
        public long Freshness { get; init; }

        /// <summary>价值评分（0..1，由产出评估 eval 结果映射；可空 = 无评分）。</summary>
        public double? ValueScore { get; init; }

        /// <summary>输出摘要（正文截断摘要，供信号传播免读正文；可空 = 未生成）。</summary>
        public string? OutputDigest { get; init; }

        /// <summary>显式焦点标记（恒最前）。</summary>
        public bool Sticky { get; init; }

        /// <summary>显式优先级（同级 sticky 内先按此；越大越前）。</summary>
        public int Priority { get; init; }
    }
}
