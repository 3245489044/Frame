using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>
/// 并发成本门禁：C_group ≤ C_single ∧ 预算 ∧ 容量。判据主口径 = token（可复算）；
/// 装配价格源时同步填 <see cref="CostEstimate.AmountMicros"/>（金额口径 = token × 声明单价）。
/// <para>配置只读不可变快照；价格/容量经端口注入（零 Frame 依赖）。</para>
/// </summary>
public sealed class ConcurrencyBudgetGate : IConcurrencyBudgetGate
{
    private const decimal MicrosPerCurrencyUnit = 1_000_000m;
    private readonly ICostPriceSource? _priceSource;
    private readonly Func<IConcurrencyCapacitySource?>? _capacity;
    private readonly ConcurrencyOptions _options;

    /// <param name="options">策略快照（组合根生成）。</param>
    /// <param name="priceSource">价格源（null ⇒ 金额口径 Unknown，仅 token 判据）。</param>
    public ConcurrencyBudgetGate(ConcurrencyOptions options, ICostPriceSource? priceSource = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _priceSource = priceSource;
    }

    /// <summary>容量单源装配：名额上限与实时可用 slot 一律读池（禁止门禁另读配置）。</summary>
    public ConcurrencyBudgetGate(
        ConcurrencyOptions options, ICostPriceSource? priceSource, Func<IConcurrencyCapacitySource?> capacity)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _priceSource = priceSource;
        _capacity = capacity;
    }

    public Task<ConcurrencyAdmission> EvaluateAsync(
        IReadOnlyList<SplitMember> members, long? singleEstimateTokens, CancellationToken ct = default)
    {
        var options = _options.Budget;
        var n = members?.Count ?? 0;
        // 容量单源：池的上限与实时 slot 为唯一真值；未装配容量来源才回退声明名额。
        var capacity = _capacity?.Invoke();
        var maxSlots = capacity?.MaxConcurrency ?? options.MaxConcurrentCalls;
        var slots = capacity?.AvailableSlots ?? options.MaxConcurrentCalls;

        if (members is null || n == 0)
            return Deny("CONCURRENCY_EMPTY", 0, 0, 0, slots, "no members");

        var unknown = members.Any(static m => m.EstTokens <= 0);
        if (unknown && !options.AllowUnknownCost)
            return Deny("CONCURRENCY_COST_UNKNOWN", 0, 0, n, slots, "member EstTokens unavailable");

        var body = members.Sum(static m => Math.Max(0, m.EstTokens));
        var overhead = (long)Math.Ceiling(n * Math.Max(1, options.UnitTokensPerNode) * Math.Max(0, options.CoordOverheadRatio));
        var reuseRefs = members.SelectMany(static m => m.NodeRefs ?? Array.Empty<string>())
            .GroupBy(static r => r, StringComparer.Ordinal)
            .Sum(static g => Math.Max(0, g.Count() - 1));
        var reuseSaving = (long)Math.Floor(reuseRefs * Math.Max(0, options.UnitTokensPerNode)
                                         * Math.Clamp(options.ReuseProbDefault, 0, 1));
        var groupTokens = Math.Max(0, body + overhead - reuseSaving);

        var singleTokens = singleEstimateTokens is > 0
            ? singleEstimateTokens.Value
            : (long)Math.Ceiling(groupTokens * Math.Max(1, options.SinglePenaltyFactor));

        // 金额口径分离：成员声明 Provider/Model 时按成员计价（input/output/cache 分价），否则按缺省声明计价。
        var hasMemberPricing = members!.Any(static m =>
            !string.IsNullOrWhiteSpace(m.Provider) || !string.IsNullOrWhiteSpace(m.Model));
        long? groupMicros;
        string currency;
        string priceBasis;
        var priced = hasMemberPricing
            ? TryPriceMembers(members!, groupTokens, out groupMicros, out currency, out priceBasis)
            : TryPrice(groupTokens, out groupMicros, out currency, out priceBasis);
        if (!priced
            && _priceSource is not null && !options.AllowUnknownCost)
        {
            return Deny("CONCURRENCY_COST_UNKNOWN", groupTokens, singleTokens, n, slots,
                $"price unavailable (provider={options.Provider ?? "-"}, model={options.Model ?? "-"})");
        }
        TryPrice(singleTokens, out var singleMicros, out _, out _);

        var groupEst = new CostEstimate(groupMicros, groupTokens, currency, priceBasis);
        var singleEst = new CostEstimate(singleMicros, singleTokens, currency, priceBasis);

        if (options.BudgetTokens > 0 && groupTokens > options.BudgetTokens)
            return Task.FromResult(new ConcurrencyAdmission(false, "CONCURRENCY_BUDGET_EXHAUSTED", groupEst, singleEst, n, slots,
                $"group={groupTokens} > budget={options.BudgetTokens}"));
        if (maxSlots <= 0 || n > maxSlots)
            return Task.FromResult(new ConcurrencyAdmission(false, "CONCURRENCY_CAPACITY_FULL", groupEst, singleEst, n, slots,
                $"requested={n} > poolMax={maxSlots}"));
        if (slots <= 0 || n > slots)
            return Task.FromResult(new ConcurrencyAdmission(false, "CONCURRENCY_CAPACITY_FULL", groupEst, singleEst, n, slots,
                $"requested={n} > available={slots}"));
        if (groupTokens > singleTokens)
            return Task.FromResult(new ConcurrencyAdmission(false, "CONCURRENCY_NOT_COST_EFFECTIVE", groupEst, singleEst, n, slots,
                $"group={groupTokens} > single={singleTokens} (reuseSaving={reuseSaving}, overhead={overhead})"));

        return Task.FromResult(new ConcurrencyAdmission(true, "ADMITTED", groupEst, singleEst, n, slots,
            $"group={groupTokens} <= single={singleTokens}"));
    }
    /// <summary>金额口径换算：input/output/cache 分价（比例来自声明）；缺价 ⇒ false。成员可覆盖 provider/model。</summary>
    private bool TryPrice(long tokens, out long? amountMicros, out string currency, out string basis,
        string? provider = null, string? model = null)
    {
        amountMicros = null;
        currency = "USD";
        basis = "token";
        var options = _options.Budget;
        var providerId = string.IsNullOrWhiteSpace(provider) ? options.Provider : provider;
        var modelId = string.IsNullOrWhiteSpace(model) ? options.Model : model;
        if (_priceSource is null
            || string.IsNullOrWhiteSpace(providerId)
            || string.IsNullOrWhiteSpace(modelId)
            || !_priceSource.TryGet(providerId!, modelId!, DateOnly.FromDateTime(DateTime.UtcNow), out var quote))
        {
            return false;
        }

        currency = quote.Currency;
        basis = "declarative-price";
        amountMicros = ComputeAmount(tokens, quote, options);
        return true;
    }

    /// <summary>成员级计价汇总（成员各自 provider/model）+ 剩余协调开销按缺省价。</summary>
    private bool TryPriceMembers(IReadOnlyList<SplitMember> members, long groupTokens,
        out long? amountMicros, out string currency, out string basis)
    {
        amountMicros = null;
        currency = "USD";
        basis = "token";
        if (_priceSource is null) return false;

        long total = 0;
        long memberTokens = 0;
        foreach (var member in members)
        {
            if (!TryPrice(Math.Max(0, member.EstTokens), out var memberMicros, out var memberCurrency,
                    out _, member.Provider, member.Model))
                return false;
            total += memberMicros ?? 0;
            memberTokens += Math.Max(0, member.EstTokens);
            currency = memberCurrency;
        }

        var overhead = Math.Max(0, groupTokens - memberTokens);
        if (overhead > 0)
        {
            if (!TryPrice(overhead, out var overheadMicros, out _, out _)) return false;
            total += overheadMicros ?? 0;
        }

        amountMicros = total;
        basis = "declarative-price";
        return true;
    }

    /// <summary>单条金额：input/output/cache 三路分价后求和（micros）。</summary>
    private static long ComputeAmount(long tokens, CostQuote quote, ConcurrencyBudgetOptions options)
    {
        var safeTokens = Math.Max(0, tokens);
        var cacheRead = (long)Math.Floor(safeTokens * Math.Clamp(options.CacheReadRatio, 0, 1));
        var input = Math.Max(0, safeTokens - cacheRead);
        var output = (long)Math.Ceiling(safeTokens * Math.Clamp(options.OutputTokenRatio, 0, 1));
        var cacheRate = quote.CachedInputPerToken ?? quote.InputPerToken;
        var amount = input * quote.InputPerToken
                   + output * quote.OutputPerToken
                   + cacheRead * cacheRate;
        return (long)Math.Ceiling(amount * MicrosPerCurrencyUnit);
    }

    private static Task<ConcurrencyAdmission> Deny(
        string code, long groupTokens, long singleTokens, int requested, int slots, string detail)
        => Task.FromResult(new ConcurrencyAdmission(
            false, code,
            new CostEstimate(null, groupTokens, "token", "declarative"),
            new CostEstimate(null, singleTokens, "token", "declarative"),
            requested, slots, detail));
}
