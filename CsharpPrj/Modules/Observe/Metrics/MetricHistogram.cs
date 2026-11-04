using System.Diagnostics;
using System.Numerics;

namespace Modules.Observe.Metrics;

/// <summary>
/// log2 桶直方图：按时间戳差值的最高有效位落桶，O(1) 记录、O(桶数) 求分位，无排序、无分配。
/// 分位值用「桶上界」近似，误差不超过当前桶宽；桶覆盖到 2^47 ticks（约数小时），足够所有帧内阶段。
/// </summary>
public sealed class MetricHistogram
{
    public const int BucketCount = 48;

    private readonly long[] _buckets = new long[BucketCount];

    public long Total { get; private set; }
    public long MinTicks { get; private set; }
    public long MaxTicks { get; private set; }

    public void Record(long ticks)
    {
        if (ticks <= 0)
        {
            _buckets[0]++;
            Total++;
            return;
        }

        int bucket = 63 - BitOperations.LeadingZeroCount((ulong)ticks);
        if (bucket >= BucketCount) bucket = BucketCount - 1;
        _buckets[bucket]++;
        Total++;
        if (MinTicks == 0 || ticks < MinTicks) MinTicks = ticks;
        if (ticks > MaxTicks) MaxTicks = ticks;
    }

    /// <param name="percentile">0..1，例如 0.95。</param>
    public long PercentileTicks(double percentile)
    {
        if (Total == 0) return 0;

        long target = (long)Math.Ceiling(percentile * Total);
        if (target < 1) target = 1;

        long cumulative = 0;
        for (int bucket = 0; bucket < BucketCount; bucket++)
        {
            cumulative += _buckets[bucket];
            if (cumulative >= target)
                return bucket == 0 ? 1 : (1L << (bucket + 1)) - 1;
        }

        return MaxTicks;
    }

    public double PercentileMs(double percentile) =>
        PercentileTicks(percentile) * 1000.0 / Stopwatch.Frequency;

    public double MaxMs => MaxTicks * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// 任意数值样本的 log2 桶上界近似（<c>2^(b+1)-1</c>），与 <see cref="PercentileTicks"/> 同一口径。
    /// 供“样本已精确持有、但输出要与桶直方图对齐”的通道使用（bench / EngineMetrics），
    /// 避免出现第三套百分位定义。单位随入参（ms 或 ticks）保持。
    /// </summary>
    public static double BucketUpperBoundValue(double value)
    {
        if (value <= 0) return 1;
        int bucket = (int)Math.Floor(Math.Log2(value));
        if (bucket >= BucketCount - 1) bucket = BucketCount - 2;
        return (1L << (bucket + 1)) - 1;
    }

    internal void Reset()
    {
        Array.Clear(_buckets, 0, _buckets.Length);
        Total = 0;
        MinTicks = 0;
        MaxTicks = 0;
    }
}
