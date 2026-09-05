#nullable enable

using System;
using System.Collections.Generic;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 有序策略注册表（通用框架）：注册顺序即优先级（先注册先判定），未命中回退默认策略。
/// 收敛 ControlProjectionStrategyRegistry 等"有序判定 + 默认回退"的同构注册表，
/// 新增策略 = Register，不改核心逻辑。
/// </summary>
/// <typeparam name="TStrategy">策略类型。</typeparam>
public class OrderedStrategyRegistry<TStrategy>
{
    private readonly List<TStrategy> _strategies = new();
    private readonly TStrategy? _default;

    public OrderedStrategyRegistry(TStrategy? defaultStrategy = default)
    {
        _default = defaultStrategy;
    }

    /// <summary>按注册顺序排列的策略列表（只读）。</summary>
    public IReadOnlyList<TStrategy> Strategies => _strategies;

    public int Count => _strategies.Count;

    /// <summary>默认策略（未命中回退）。</summary>
    public TStrategy? Default => _default;

    /// <summary>注册策略（去重；顺序即优先级）。</summary>
    public void Register(TStrategy strategy)
    {
        if (strategy == null) return;
        if (!_strategies.Contains(strategy))
            _strategies.Add(strategy);
    }

    /// <summary>批量注册策略（保持给定顺序）。</summary>
    public void RegisterRange(IEnumerable<TStrategy> strategies)
    {
        if (strategies == null) return;
        foreach (var s in strategies) Register(s);
    }

    /// <summary>
    /// 按注册顺序解析首个满足 <paramref name="appliesTo"/> 的策略；未命中返回默认策略。
    /// </summary>
    public TStrategy? Resolve(Func<TStrategy, bool> appliesTo)
    {
        if (appliesTo == null) return _default;
        foreach (var s in _strategies)
        {
            if (appliesTo(s)) return s;
        }
        return _default;
    }

    public bool Contains(TStrategy strategy) => _strategies.Contains(strategy);

    public void Clear() => _strategies.Clear();
}
