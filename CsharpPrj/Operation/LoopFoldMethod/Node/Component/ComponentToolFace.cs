using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Node.Component;

/// <summary>
/// 组件工具面 —— 一次装配所命中组件的声明式工具（<see cref="ComponentToolSpec"/> 列表，喂 Function-Calling 前端）
/// 与确定性执行端绑定（组件名 → <see cref="NodeComponent.ExecutorKey"/>，供执行端分发）。
/// 两者与提示词面同源于同一组组件声明（<see cref="ComponentSelection.Selected"/>），保证"提示词 + 机器工具 + 执行端"一致。
/// 框架只产出元数据；具象化为 LLM 工具 / 执行器在消费侧唯一 binder。
/// </summary>
/// <param name="Tools">命中的组件声明式工具（去重，依解析序）。</param>
/// <param name="ExecutorBindings">组件名 → 确定性执行端键（去重，依解析序）。</param>
public sealed record ComponentToolFace(
    IReadOnlyList<ComponentToolSpec> Tools,
    IReadOnlyDictionary<string, string> ExecutorBindings)
{
    /// <summary>空工具面（无命中工具 / 无执行端绑定）。</summary>
    public static readonly ComponentToolFace Empty =
        new(System.Array.Empty<ComponentToolSpec>(), new Dictionary<string, string>());
}
