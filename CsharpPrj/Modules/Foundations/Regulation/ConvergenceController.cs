using System;
using System.Collections.Generic;

namespace Modules.Regulation;

/// <summary>收敛判定结果（针对单次 drain 后仍滞留的键）。</summary>
public enum ConvergenceDecisionKind
{
    /// <summary>仍在预算内 → 允许重新调度消化。</summary>
    Retry,

    /// <summary>调用方判定不可重试（终态/阻塞）→ 移出待处理，不重新调度。</summary>
    Drop,

    /// <summary>可重试但连续预算耗尽超上限 → 终态隔离并通知调用方。</summary>
    Abandon
}

/// <summary>收敛判定（<see cref="ConvergenceDecisionKind"/> + 已累计的预算耗尽次数，1-based）。</summary>
public readonly record struct ConvergenceDecision(ConvergenceDecisionKind Kind, int Attempt);

/// <summary>
/// 收敛控制律（有状态、BCL-only）：把「单 pass 内同键连续重排风暴护栏」与
/// 「跨 drain 的按键预算耗尽重试/终态隔离」从各处调度循环中收敛为单一实现。
/// <para>
/// 职责边界：本类只做判定与计数，不持有队列、不执行工作、不调度线程；
/// 阈值一律构造期注入（禁读进程静态全局），键比较由调用方语义决定（默认 Ordinal）。
/// 非线程安全：供单消费者 drain（调用方已有重入守卫）在单线程内使用。
/// </para>
/// </summary>
public sealed class ConvergenceController
{
    private readonly int _stormThreshold;
    private readonly int _retryBudget;
    private readonly IEqualityComparer<string> _keyComparer;
    private readonly Dictionary<string, int> _exhaustions;

    private string? _lastKey;
    private int _consecutive;

    /// <param name="stormThreshold">单 pass 内同一键连续执行的最大次数；超过即判定风暴（≤0 表示不设护栏）。</param>
    /// <param name="retryBudget">跨 drain 连续预算耗尽允许重试的次数上限；超过即终态隔离（≤0 表示不允许重试）。</param>
    /// <param name="keyComparer">键比较器；null → <see cref="StringComparer.Ordinal"/>。</param>
    public ConvergenceController(
        int stormThreshold,
        int retryBudget,
        IEqualityComparer<string>? keyComparer = null)
    {
        _stormThreshold = stormThreshold;
        _retryBudget = retryBudget;
        _keyComparer = keyComparer ?? StringComparer.Ordinal;
        _exhaustions = new Dictionary<string, int>(_keyComparer);
    }

    /// <summary>当前累计预算耗尽的键数（诊断用）。</summary>
    public int TrackedKeyCount => _exhaustions.Count;

    /// <summary>开始一次 drain pass（复位同一 pass 内的连续计数）。</summary>
    public void BeginPass()
    {
        _lastKey = null;
        _consecutive = 0;
    }

    /// <summary>
    /// 记录一次键执行：返回 false 表示「同键连续执行超过风暴阈值，应终止本 pass 剩余遍历」。
    /// 返回 true 表示允许继续。
    /// </summary>
    public bool TrackExecution(string key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        if (_keyComparer.Equals(key, _lastKey))
            _consecutive++;
        else
        {
            _lastKey = key;
            _consecutive = 1;
        }

        return _stormThreshold <= 0 || _consecutive <= _stormThreshold;
    }

    /// <summary>当前同一键的连续执行次数（用于风暴日志）。</summary>
    public int ConsecutiveCount => _consecutive;

    /// <summary>
    /// 对一个 drain 后仍滞留的键做预算判定并推进计数：
    /// 不可重试 → <see cref="ConvergenceDecisionKind.Drop"/>；
    /// 可重试且累计次数超过 <c>retryBudget</c> → <see cref="ConvergenceDecisionKind.Abandon"/>（并清除其计数）；
    /// 否则 → <see cref="ConvergenceDecisionKind.Retry"/>（计数 +1）。
    /// </summary>
    public ConvergenceDecision ClassifyRemaining(string key, bool retryable)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        int count = _exhaustions.TryGetValue(key, out var previous) ? previous + 1 : 1;

        if (!retryable)
        {
            _exhaustions.Remove(key);
            return new ConvergenceDecision(ConvergenceDecisionKind.Drop, count);
        }

        if (count > _retryBudget)
        {
            _exhaustions.Remove(key);
            return new ConvergenceDecision(ConvergenceDecisionKind.Abandon, count);
        }

        _exhaustions[key] = count;
        return new ConvergenceDecision(ConvergenceDecisionKind.Retry, count);
    }

    /// <summary>队列已清空（全部收敛）→ 复位所有按键计数，避免健康键积累误判。</summary>
    public void Reset() => _exhaustions.Clear();

    /// <summary>移除单个键的计数（键被终态移出时调用）。</summary>
    public void Forget(string key) => _exhaustions.Remove(key);
}
