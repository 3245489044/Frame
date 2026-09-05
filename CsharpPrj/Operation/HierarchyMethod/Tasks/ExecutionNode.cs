using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Tasks
{
    /// <summary>
    /// 执行节点 —— 把 LLM / 执行相关逻辑封装为一个自包含节点，由任务（<see cref="TaskRecord"/>）作为**属性**直接携带
    /// （task 携带该节点，相当于任务的一个属性；task 本身不调用 LLM，节点由执行器在消费时解析执行）。
    /// 对齐既有"节点"概念（工作流步骤 = 子智能体函数；任务关联节点 relatedNodes）：本节点即"该任务如何执行"的执行单元。
    /// 提示词/轮数/停止规则等 LLM 细节不内嵌，经 <see cref="WorkflowConfigKey"/> 引用外部工作流/配置
    /// （Config/workflows/*.json、ur_config），保持节点轻量、可序列化、随 task 持久化。
    /// </summary>
    public sealed class ExecutionNode
    {
        /// <summary>执行器句柄（如 "stateless-answer"）；空 = 按导演模式/路由默认解析。</summary>
        public string? ExecutorId { get; set; }

        /// <summary>调度导演模式（Hybrid=LLM / Config·Deterministic=零 LLM）；null = 缺省（零 LLM 倾向）。</summary>
        public SchedulingMode? DirectorMode { get; set; }

        /// <summary>配置式调度使用的 ur_config 规则键。</summary>
        public string? RulesKey { get; set; }

        /// <summary>工作流/执行器配置键（提示词计划、轮数、停止规则等 LLM 细节的外部来源）。</summary>
        public string? WorkflowConfigKey { get; set; }

        /// <summary>是否显式指定了执行器/策略（true 时执行器解析未命中即 fail-fast 报错，而非静默回退首个执行器）。</summary>
        public bool ExplicitExecutor { get; set; }

        /// <summary>执行器专属载荷（如 answer 的提示词计划；task 不理解其含义，仅透传给执行器）。</summary>
        public string? Payload { get; set; }
    }
}
