using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Node.Session;

/// <summary>
/// 装配请求 —— 一次装配的声明输入：作用域 + 作用域键 + 点名组件 + 步骤键。
/// 动态上下文（历史窗口 / task 动态内容 / 上步产出）不入本载体，经引擎的动态节点入参
/// （<see cref="INodeAssemblyEngine.Assemble"/>）或动态源（<see cref="INodeAssemblyEngine.AssembleAsync{TState,TVertex}"/>）注入。
/// 点名组件 = 待注入的具名组件（如按 stepKey/策略选中的技能）；null/空 = 仅 AlwaysOn 注入。
/// </summary>
/// <param name="Scope">装配作用域（子智能体一步 / 任务轮 / 历史轮）。</param>
/// <param name="ScopeId">作用域键（子智能体步骤键 / taskKey / historyId；非空约定）。</param>
/// <param name="RequestedNames">点名注入的组件名（可选；缺省 = 仅 AlwaysOn）。</param>
/// <param name="StepKey">本步步骤键（可选；动态源收集入口，缺省回退 <paramref name="ScopeId"/>）。</param>
public sealed record AssemblyRequest(
    AssemblyScope Scope,
    string ScopeId,
    IReadOnlyList<string>? RequestedNames = null,
    string? StepKey = null);
