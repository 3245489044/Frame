using System.Collections.Generic;
using Operation.LoopFoldMethod.Node.Model;

namespace Operation.LoopFoldMethod.Node.Component;

/// <summary>
/// 组件声明 —— "一个可复用装配单元 = 一次声明"（SingleImplementation / TH-12 单一所有者）。
/// 一个组件同时声明其提示词源（<see cref="PromptText"/>）、声明式机器工具元数据（<see cref="Tool"/>）
/// 与确定性执行端键（<see cref="ExecutorKey"/>），使"同一声明 → 一致的提示词面 + 机器工具面 + 执行端绑定"
/// 由单一来源给出，消费方（子智能体 / task / history 各装配点）不再各自拼装。
/// 框架零 LLM、Agent 无关：只持字符串 / 元数据，不含 LlmTool / 执行闭包 / 任何 Agent 类型；
/// 具象化（ToolSpec→LLM 工具、ExecutorKey→执行器）留消费侧唯一 binder。
/// </summary>
/// <param name="Name">组件名（注册表主键；点名注入依据）。</param>
/// <param name="Category">组件类别（决定默认槽位：Base→Head、Skill→Tail、其余→Body）。</param>
/// <param name="Order">段内序（排序键，同序稳定）。</param>
/// <param name="AlwaysOn">是否常驻注入（True = 每次装配默认注入，如 L0 稳定底座段）。</param>
/// <param name="PromptText">提示词源（可选；null/空 = 纯工具/执行组件，无散文提示段）。</param>
/// <param name="Tool">声明式机器工具元数据（可选；喂 LLM Function-Calling 前端的声明）。</param>
/// <param name="ExecutorKey">确定性执行端键（可选；字符串指向消费侧执行端，不引执行端类型）。</param>
/// <param name="Tags">上下文关键词自动选择标签（可选；用于按 goal/userInput/scopeId 打分）。</param>
/// <param name="Trigger">显式触发键（可选；命中权重高于 Tags）。</param>
/// <param name="OnDemand">是否按需注入（True = 不进关键词自动选择池，仅点名注入，类似懒加载语义）。</param>
public sealed record NodeComponent(
    string Name,
    NodeCategory Category,
    int Order,
    bool AlwaysOn = false,
    string? PromptText = null,
    ComponentToolSpec? Tool = null,
    string? ExecutorKey = null,
    IReadOnlyList<string>? Tags = null,
    string? Trigger = null,
    bool OnDemand = false);
