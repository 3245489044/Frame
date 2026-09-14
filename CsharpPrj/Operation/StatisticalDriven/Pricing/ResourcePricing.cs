namespace Operation.StatisticalDriven.Pricing;

/// <summary>
/// 计算资源单价（设计文档 7.3）。缺省全为 0 = 默认不计计算资源成本；
/// 消费方按自身计量口径（如 cloud 单价）显式配置。
/// </summary>
public sealed record ResourcePricing
{
    /// <summary>CPU 秒单价（$/cpu-second）。</summary>
    public decimal CpuPerSecond { get; init; } = 0m;

    /// <summary>内存 GB·秒单价（$/gb-second）。</summary>
    public decimal MemoryPerGbSecond { get; init; } = 0m;

    /// <summary>磁盘读写字节单价（$/byte）。</summary>
    public decimal IoPerByte { get; init; } = 0m;

    public static readonly ResourcePricing Zero = new();
}