using System;
using System.Collections.Generic;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 声明失败姿态策略（G3）：把「同一份声明错误」的五路分叉（抛 / 空集+诊断 / 返回 null 回退 / 跳过 / 可见化回退）
/// 收敛为**一个枚举**，由组合根一处注入。
/// </summary>
public enum DeclarationGateStrategy
{
    /// <summary>严格（生产）：编译/发射 error ⇒ 抛（fail-fast，禁静默）。</summary>
    Throw,

    /// <summary>灰度/开发：失败 ⇒ 返回空集，诊断由调用方既有的诊断出口可见化（不静默）。</summary>
    EmptyWithDiagnostics,

    /// <summary>迁移回退：失败 ⇒ 返回显式回退值（须登记，可观测）。</summary>
    NullFallback,
}

/// <summary>
/// <b>单一声明门</b>（G3）：声明装载失败姿态的**唯一裁决点**。
/// <para>
/// 消费方以 <see cref="Resolve{T}"/> 归一失败：装载函数返回 <c>(值, 错误列表)</c>，门按
/// <see cref="Strategy"/> 择一动作（抛 / 空 / 回退）——五条路径不再各自 self-driven 失败策略。
/// </para>
/// </summary>
public sealed class DeclarationGate
{
    /// <summary>生产（严格）：error ⇒ 抛。</summary>
    public static readonly DeclarationGate Strict = new(DeclarationGateStrategy.Throw);

    /// <summary>开发/灰度：error ⇒ 空集（诊断可见）。</summary>
    public static readonly DeclarationGate Development = new(DeclarationGateStrategy.EmptyWithDiagnostics);

    /// <summary>迁移回退：error ⇒ 回退值。</summary>
    public static readonly DeclarationGate Fallback = new(DeclarationGateStrategy.NullFallback);

    /// <summary>当前策略。</summary>
    public DeclarationGateStrategy Strategy { get; }

    /// <summary>由策略构造。</summary>
    /// <param name="strategy">失败姿态策略。</param>
    public DeclarationGate(DeclarationGateStrategy strategy) => Strategy = strategy;

    /// <summary>
    /// 归一失败姿态：装载函数返回 <c>(值, 错误)</c>；无错误直接返回值，有错误按 <see cref="Strategy"/> 择一。
    /// </summary>
    /// <typeparam name="T">装载结果类型。</typeparam>
    /// <param name="target">目标标识（错误报文归因）。</param>
    /// <param name="load">装载函数（返回结果 + 错误列表；错误列表为空 = 成功）。</param>
    /// <param name="empty">空集值（EmptyWithDiagnostics 用）。</param>
    /// <param name="fallback">回退值（NullFallback 用；缺省 = <paramref name="empty"/>）。</param>
    public T Resolve<T>(
        string target,
        Func<(T Value, IReadOnlyList<string> Errors)> load,
        T empty,
        T? fallback = default)
    {
        ArgumentNullException.ThrowIfNull(load);

        var (value, errors) = load();
        if (errors is null || errors.Count == 0) return value;

        return Strategy switch
        {
            DeclarationGateStrategy.Throw => throw new InvalidOperationException(
                $"[声明门] {target} 装载失败（{errors.Count} 项）：{string.Join("; ", errors)}"),
            DeclarationGateStrategy.NullFallback => fallback is null ? empty : fallback,
            _ => empty,
        };
    }
}
