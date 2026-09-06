using System.Collections.Generic;
using Operation.LoopFoldMethod.Node.Assemble;
using Operation.LoopFoldMethod.Node.Component;

namespace Operation.LoopFoldMethod.Node.Session;

/// <summary>
/// 装配产物 —— 一次装配的一致多面结果：
/// ① <see cref="Assembly"/>：头/中/尾三段节点装配（提示词/上下文的编排底料，供消费域投影器投影）；
/// ② <see cref="AppliedNames"/>：本次实际命中并注入的组件名（AlwaysOn + 点名命中）；
/// ③ <see cref="UnmetNames"/>：点名但注册表缺失的组件名（未注册/懒加载未挂载，供诊断）；
/// ④ <see cref="ToolFace"/>：命中的组件声明式工具 + 执行端绑定（与提示词面同源）。
/// 纯装配只读产物，不落库；投影为具体形态（消息 / 工具 / 文档）由消费域完成。
/// </summary>
/// <param name="Assembly">三段节点装配。</param>
/// <param name="AppliedNames">实际命中并注入的组件名。</param>
/// <param name="UnmetNames">点名但注册表缺失的组件名。</param>
/// <param name="ToolFace">组件工具面（声明式工具 + 执行端绑定）。</param>
public sealed record AssemblyOutcome(
    NodeAssembly Assembly,
    IReadOnlyList<string> AppliedNames,
    IReadOnlyList<string> UnmetNames,
    ComponentToolFace ToolFace);
