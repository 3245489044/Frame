using System;
using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Termination;

/// <summary>
/// 终止策略链 —— 优先级即顺序，首个终值短路（表驱动，无常驻状态）。
/// 默认顺序（结构与语义事实先于启发式）：宿主关闭 → 失败熔断 → 总预算 → 空闲 → 同指纹 → 空转。
/// </summary>
public sealed class TerminationPolicyChain : ITerminationPolicy
{
    private readonly ITerminationPolicy[] _policies;

    public TerminationPolicyChain(params ITerminationPolicy[] policies)
        => _policies = policies ?? Array.Empty<ITerminationPolicy>();

    /// <summary>链路（只读，供装配期审计/门禁核对）。</summary>
    public IReadOnlyList<ITerminationPolicy> Policies => _policies;

    public TerminationVerdict Decide(in TerminationContext context)
    {
        foreach (var policy in _policies)
        {
            var verdict = policy.Decide(context);
            if (verdict.IsTerminal) return verdict;
        }
        return TerminationVerdict.Continue;
    }

    /// <summary>按选项装配默认链（HaltOnFailure=false 时不装配失败熔断策略）。</summary>
    public static TerminationPolicyChain Default(TerminationOptions? options = null)
    {
        var opt = options ?? TerminationOptions.Default;
        var policies = new List<ITerminationPolicy>
        {
            new HostClosedPolicy(),
            new BudgetTerminationPolicy(),
            new IdleTerminationPolicy(),
            new FingerprintTerminationPolicy(opt.SameFingerprintRounds),
            new StagnationTerminationPolicy(opt.TerminateOnStagnation),
        };

        if (opt.HaltOnFailure)
            policies.Insert(1, new FailureHaltPolicy());

        return new TerminationPolicyChain(policies.ToArray());
    }
}
