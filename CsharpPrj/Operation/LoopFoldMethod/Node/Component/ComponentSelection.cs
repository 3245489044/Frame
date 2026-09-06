using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Node.Component;

/// <summary>
/// 注册表解析结果 —— <see cref="Selected"/>（AlwaysOn 依 Order 升序 + 点名命中的组件，去重）
/// 与 <see cref="UnmetNames"/>（点名但未注册的组件名，供诊断；未命中不阻断，兼容懒加载语义）。
/// </summary>
/// <param name="Selected">本次装配待注入的组件（去重有序）。</param>
/// <param name="UnmetNames">点名但注册表缺失的组件名（未注册/懒加载未挂载）。</param>
public sealed record ComponentSelection(
    IReadOnlyList<NodeComponent> Selected,
    IReadOnlyList<string> UnmetNames)
{
    /// <summary>空解析结果（无组件、无未命中）。</summary>
    public static readonly ComponentSelection Empty =
        new(System.Array.Empty<NodeComponent>(), System.Array.Empty<string>());

    /// <summary>是否无任何选中组件。</summary>
    public bool IsEmpty => Selected.Count == 0 && UnmetNames.Count == 0;
}
