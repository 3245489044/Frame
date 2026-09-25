using System;
using System.Collections.Generic;

namespace Share.AiScheduling;

/// <summary>
/// 通用导演解析器 —— 把「按调度模式选导演」从消费方（如组合根的 switch）收敛为通用机制；
/// 原 Operation.LoopFoldMethod.Director.DirectorResolver 迁入后曾中转于 Agent 侧调度命名空间，
/// 现按「机制通用、载荷为域」口径归位调度框架层。
/// <para>
/// 本类型只认识 <see cref="IStateDirector"/> 端口与「键 → 工厂」注册表：<b>键类型由消费方给出</b>
/// （如领域调度模式枚举），框架不引用任何域枚举；也不认识任何具体导演实现
/// （LlmStateDirector / ConfigStateDirector / NoopStateDirector 等留在消费域）。
/// </para>
/// <para>
/// LLM-free 边界：空工厂注册表（或键未注册）返回 null，消费方按「无导演 = 确定性默认」fail-safe 处理；
/// LlmStateDirector 只是可注册工厂之一 —— 框架不强制 LLM，而非不提供 LLM 能力。
/// </para>
/// </summary>
public static class DirectorResolver
{
    /// <summary>由工厂注册表构建导演解析委托：按键查找工厂并实例化。</summary>
    /// <typeparam name="TKey">注册表键类型（消费方的模式/场景枚举或标识）。</typeparam>
    /// <param name="factories">键 → 导演工厂注册表（消费方装配，如 Hybrid→LlmStateDirector、Deterministic→NoopStateDirector）。</param>
    /// <returns>解析委托：键未注册返回 null（fail-safe）。</returns>
    public static Func<TKey, IStateDirector?> ResolveFrom<TKey>(
        IReadOnlyDictionary<TKey, Func<IStateDirector>> factories)
        where TKey : notnull
        => key => factories.TryGetValue(key, out var factory) ? factory() : null;
}
