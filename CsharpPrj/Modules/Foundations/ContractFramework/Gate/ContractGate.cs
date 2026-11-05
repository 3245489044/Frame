using ContractFramework.Abstractions;
using ContractFramework.Registry;

namespace ContractFramework.Gate;

/// <summary>
/// 契约执行门（唯一执行入口 + **单一抛出点**）。
/// <para>按 Tier 执行：Build/Gate 期由门禁脚本调用；Assembly/Runtime 由宿主调用。</para>
/// <para>三处唯一：抛出点唯一（<see cref="Enforce"/>）/ 执行体注册唯一（<see cref="ContractRegistry"/>）/ 声明唯一（<c>contracts.json</c>）。</para>
/// </summary>
public static class ContractGate
{
    /// <summary>按 Tier 执行全部匹配契约（逐契约三值；聚合裁决；<c>⊥</c> 恒不放行）。</summary>
    public static ContractResult Execute(
        IReadOnlyList<ContractDescriptor>? contracts,
        ContractTier tier,
        IContractContext? context,
        ContractRegistry? registry = null)
    {
        if (contracts is null || contracts.Count == 0)
        {
            return ContractResult.Vacuous;
        }

        var effectiveContext = context ?? new ContractContext();
        var outcomes = new List<ContractOutcome>();
        foreach (var contract in contracts)
        {
            if (contract is null || contract.Tier != tier)
            {
                continue;
            }

            outcomes.Add(Evaluate(contract, tier, effectiveContext, registry));
        }

        return outcomes.Count == 0 ? ContractResult.Vacuous : ContractResult.From(outcomes);
    }

    /// <summary>
    /// 装配期强制：<c>FailFast</c> 违例 ∨ 任一 <c>⊥</c>（不可判定）⇒ 单一抛出点
    /// （含 <c>ContractUnverifiable</c> 清单与修复引导）。<c>Block</c> 不抛，随结果返回由调用方 fail-closed。
    /// </summary>
    public static void Enforce(
        IReadOnlyList<ContractDescriptor>? contracts,
        ContractTier tier,
        IContractContext? context,
        ContractRegistry? registry = null)
    {
        var result = Execute(contracts, tier, context, registry);

        var failFast = result.Outcomes
            .Where(o => o.Verdict == ContractVerdict.False && o.Effect == ContractEffect.FailFast)
            .SelectMany(o => o.Violations)
            .ToArray();

        var unverifiable = result.Unverifiable;
        if (failFast.Length == 0 && unverifiable.Count == 0)
        {
            return;
        }

        throw new ContractViolationException(
            ContractViolationException.Compose(failFast, unverifiable),
            failFast,
            unverifiable);
    }

    private static ContractOutcome Evaluate(
        ContractDescriptor contract,
        ContractTier tier,
        IContractContext context,
        ContractRegistry? registry)
    {
        if (!ContractWellFormedness.IsWellFormed(contract, out var reason))
        {
            return Bottom(contract, tier, $"元契约不合格：{reason}");
        }

        var host = registry?.ResolveHost(contract.Host);
        if (host is null)
        {
            var impl = contract.Host?.Impl ?? "(缺 host)";
            return Bottom(contract, tier, $"未注册执行体（host.impl={impl}）");
        }

        bool carrierReady;
        try
        {
            carrierReady = host.CarrierReady(contract, context);
        }
        catch (Exception ex)
        {
            return Bottom(contract, tier, $"载体就绪检查异常：{ex.Message}");
        }

        if (!carrierReady)
        {
            return Bottom(contract, tier, "载体不齐备（结构事实缺失）");
        }

        ContractResult result;
        try
        {
            result = host.Evaluate(contract, context);
        }
        catch (Exception ex)
        {
            return Bottom(contract, tier, $"执行体异常：{ex.Message}");
        }

        var verdict = result?.Verdict ?? ContractVerdict.Bottom;
        var violations = result?.Violations ?? [];
        var note = verdict == ContractVerdict.Bottom ? "执行体判 ⊥（不可判定）" : null;
        return new ContractOutcome(contract.Id, tier, contract.Effect, verdict, violations, note);
    }

    private static ContractOutcome Bottom(ContractDescriptor contract, ContractTier tier, string reason)
        => new(contract.Id, tier, contract.Effect, ContractVerdict.Bottom, [], reason);
}
