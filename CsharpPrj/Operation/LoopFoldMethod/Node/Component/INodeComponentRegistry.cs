using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Node.Component;

/// <summary>
/// 组件注册表端口 —— name → <see cref="NodeComponent"/> 的单一映射（唯一装配来源）。
/// 一个能力/一段静态提示只在此登记一次，消费方不再各自 Register/拼装（对齐 <see cref="ComponentSelection"/> 的 SingleImplementation）。
/// 重复登记同名组件 = 装配漂移，由实现 fail-fast（TH-12 duplicate）。注册表不含执行闭包，纯声明元数据。
/// </summary>
public interface INodeComponentRegistry
{
    /// <summary>登记一个组件（唯一落点；同名重复登记应抛异常）。</summary>
    void Register(NodeComponent component);

    /// <summary>取组件（未注册 → 抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>）。</summary>
    NodeComponent Get(string name);

    /// <summary>尝试取组件（未注册 → false）。</summary>
    bool TryGet(string name, out NodeComponent component);

    /// <summary>
    /// 解析待注入组件：AlwaysOn（依 Order 升序）+ 点名命中；点名但未注册 → 记入 <see cref="ComponentSelection.UnmetNames"/>（不阻断）。
    /// </summary>
    ComponentSelection ResolveSelected(IReadOnlyCollection<string>? requestedNames);
}
