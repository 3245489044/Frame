using CallCenter.Cost.Instrumentation;
using CallCenter.Cost.Pricing;

namespace CallCenter.Cost.Costing;

/// <summary>
/// 工具调用计价：次数 + 资源成本。
/// 次数单价缺省 ⇒ 次数项为 0（仍计数），不把整单标 Unknown。
/// 重试/失败成本为信息项（已含在 PhysicalCount 中），不重复计入 Amount。
/// </summary>
public sealed class ToolCallCostCalculator
{
    private readonly ComputeCostCalculator _compute = new();

    public ToolCallCost Calculate(ToolCallUsage usage, PriceQuote quote)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(quote);

        var items = new List<CostBreakdownItem>();
        var perCall = quote.PerCallPrice ?? 0m;
        items.Add(new CostBreakdownItem("per_call", usage.PhysicalCount * perCall,
            quote.PerCallPrice is null ? "per_call_price_missing:0" : null));

        CostResult? computeCost = null;
        if (usage.Compute is not null)
        {
            computeCost = _compute.Calculate(usage.Compute, quote);
            if (computeCost.IsKnown)
            {
                items.AddRange(computeCost.Breakdown);
            }
            else
            {
                // 有资源用量但缺价 ⇒ 整单 Unknown（次数项仍可算，但不能冒充完整成本）
                return new ToolCallCost(CostResult.Unknown(quote.Currency), null, null);
            }
        }

        var total = CostResult.Known(quote.Currency, items, CostSource.Measured);
        var retryCost = usage.RetryCount * perCall;
        var failureCost = usage.FailureCount * perCall;
        return new ToolCallCost(total, retryCost, failureCost);
    }
}

/// <summary>工具调用成本结果：基础成本 + 重试/失败信息项。</summary>
public sealed record ToolCallCost(CostResult Cost, decimal? RetryCost, decimal? FailureCost);
