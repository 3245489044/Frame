using CallCenter.Cost.Metering;
using CallCenter.Cost.Pricing;

namespace CallCenter.Cost.Costing;

/// <summary>
/// 计算资源计价器：CPU / 内存 / 磁盘。
/// 缺单价 ⇒ Unknown；缺用量字段 ⇒ 对应项缺失，不写 0。
/// </summary>
public sealed class ComputeCostCalculator
{
    public CostResult Calculate(ComputeUsage usage, PriceQuote quote)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(quote);

        var items = new List<CostBreakdownItem>();

        if (usage.CpuTotalMs is not null && quote.CpuPerSecond is not null)
        {
            items.Add(new CostBreakdownItem(
                "cpu",
                usage.CpuTotalMs.Value / 1000m * quote.CpuPerSecond.Value,
                usage.Basis));
        }

        if (usage.MemoryGbSeconds is not null && quote.MemoryPerGbSecond is not null)
        {
            items.Add(new CostBreakdownItem(
                "memory",
                (decimal)usage.MemoryGbSeconds.Value * quote.MemoryPerGbSecond.Value,
                usage.MemoryBasis == MemoryBasis.PeakXDuration ? "peak_x_duration" : "sampled_integral"));
        }

        var disk = CalculateDisk(usage, quote);
        if (disk is not null)
        {
            items.Add(disk);
        }

        // 有资源用量但对应单价缺失 ⇒ 成本 Unknown（不代表免费）
        var hasUsage = usage.CpuTotalMs is not null
            || usage.MemoryGbSeconds is not null
            || usage.IoWriteBytes is not null
            || usage.DiskFootprintDeltaBytes is not null;
        var missingPrice = (usage.CpuTotalMs is not null && quote.CpuPerSecond is null)
            || (usage.MemoryGbSeconds is not null && quote.MemoryPerGbSecond is null)
            || (NeedsDiskPrice(usage) && quote.DiskPerGbWrite is null && quote.DiskPerGb is null);

        if (hasUsage && missingPrice)
        {
            return CostResult.Unknown(quote.Currency);
        }

        return items.Count == 0
            ? CostResult.Unknown(quote.Currency)
            : CostResult.Known(quote.Currency, items, CostSource.Measured);
    }

    private static CostBreakdownItem? CalculateDisk(ComputeUsage usage, PriceQuote quote)
    {
        var throughput = usage.DiskMode is DiskMeterMode.Throughput or DiskMeterMode.Both
            && usage.IoWriteBytes is not null
            && quote.DiskPerGbWrite is not null
                ? usage.IoWriteBytes.Value / 1_000_000_000m * quote.DiskPerGbWrite.Value
                : (decimal?)null;

        var footprintGb = usage.DiskMode is DiskMeterMode.Footprint or DiskMeterMode.Both
            && usage.DiskFootprintDeltaBytes is not null
                ? Math.Max(0, usage.DiskFootprintDeltaBytes.Value) / 1_000_000_000m
                : (decimal?)null;
        var footprint = footprintGb is not null && quote.DiskPerGb is not null
            ? footprintGb.Value * quote.DiskPerGb.Value
            : (decimal?)null;

        if (throughput is null && footprint is null)
        {
            return null;
        }

        return new CostBreakdownItem(
            throughput is not null && footprint is not null ? "disk" :
            throughput is not null ? "disk_write" : "disk_footprint",
            (throughput ?? 0) + (footprint ?? 0),
            usage.DiskMode.ToString());
    }

    private static bool NeedsDiskPrice(ComputeUsage usage)
        => usage.DiskMode is DiskMeterMode.Throughput or DiskMeterMode.Both && usage.IoWriteBytes is not null
        || usage.DiskMode is DiskMeterMode.Footprint or DiskMeterMode.Both && usage.DiskFootprintDeltaBytes is not null;
}
