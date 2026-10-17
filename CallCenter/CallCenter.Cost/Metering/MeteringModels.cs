using CallCenter.Cost.Costing;
using CallCenter.Cost.Instrumentation;
using CallCenter.Cost.Pricing;
using CallCenter.Cost.Usage;

namespace CallCenter.Cost.Metering;

/// <summary>运行实例引用（开放受控词：round/task/script/tool/llm/run）。</summary>
public sealed record InstanceRef(string Kind, string Id);

/// <summary>成本跨度类别。</summary>
public enum CostKind
{
    Llm,
    Compute,
    Tool,
}

/// <summary>内存计量口径（必须随结果落库，避免跨记录误比）。</summary>
public enum MemoryBasis
{
    /// <summary>PeakMemoryGB × WallSeconds（保守上界，零采样开销）。</summary>
    PeakXDuration,

    /// <summary>200~500ms 采样梯形积分（有采样开销）。</summary>
    SampledIntegral,
}

/// <summary>磁盘计量口径。</summary>
public enum DiskMeterMode
{
    Throughput,
    Footprint,
    Both,
}

/// <summary>计量手段可信度（回退链逐级标注）。</summary>
public static class ComputeBases
{
    public const string JobObject = "job_object";
    public const string SingleProcess = "single_process";
    public const string WallOnly = "wall_only";
    public const string SelfReported = "self_reported";
}

/// <summary>
/// 本地计算用量（PS 脚本重点）。
/// 可空字段 = 不可得（Unknown），禁填 0。
/// </summary>
public sealed record ComputeUsage(
    long WallMs,
    long? CpuUserMs,
    long? CpuKernelMs,
    long? PeakWorkingSetBytes,
    long? PeakJobMemoryBytes,
    long? IoReadBytes,
    long? IoWriteBytes,
    long? DiskFootprintDeltaBytes,
    int? ProcessCount,
    long? StdOutBytes,
    long? StdErrBytes,
    int? ExitCode,
    string Basis,
    MemoryBasis MemoryBasis = MemoryBasis.PeakXDuration,
    DiskMeterMode DiskMode = DiskMeterMode.Throughput,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    /// <summary>峰值内存（优先 Job 总提交峰值）。</summary>
    public long? PeakMemoryBytes => PeakJobMemoryBytes ?? PeakWorkingSetBytes;

    /// <summary>CPU 总量（ms）；用户态不可得 ⇒ null。</summary>
    public long? CpuTotalMs => CpuUserMs is null ? null : CpuUserMs.Value + (CpuKernelMs ?? 0);

    /// <summary>墙钟秒。</summary>
    public double WallSeconds => WallMs / 1000d;

    /// <summary>峰值内存 GB（未采集 ⇒ null）。</summary>
    public double? PeakMemoryGb => PeakMemoryBytes is null ? null : PeakMemoryBytes.Value / 1024d / 1024d / 1024d;

    /// <summary>内存 GB·秒（PeakXDuration 口径；未采集 ⇒ null）。</summary>
    public double? MemoryGbSeconds => PeakMemoryGb is null ? null : PeakMemoryGb.Value * WallSeconds;
}

/// <summary>
/// 一次成本跨度：实例 + 用量 + 价格 + 结果。
/// 幂等键 = TraceId + SpanId（重复结算不叠加）。
/// </summary>
public sealed record CostSpan(
    string SpanId,
    string? ParentSpanId,
    string? TraceId,
    InstanceRef Instance,
    CostKind Kind,
    DateTimeOffset StartedAt,
    long DurationMs,
    LlmUsage? Llm,
    ComputeUsage? Compute,
    ToolCallUsage? Tool,
    PriceQuote? Price,
    CostResult? Cost,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    /// <summary>幂等键（缺 TraceId 时退化为 SpanId）。</summary>
    public string IdempotencyKey => string.IsNullOrWhiteSpace(TraceId)
        ? SpanId
        : $"{TraceId}:{SpanId}";
}

/// <summary>通用计量端口（进程内动作包装）。</summary>
public interface ICostMeter
{
    ValueTask<CostSpan> MeasureAsync(
        InstanceRef instance,
        Func<CancellationToken, ValueTask> body,
        CancellationToken ct = default);
}

/// <summary>进程成本计量端口（PS / 子进程）。</summary>
public interface IProcessCostMeter
{
    ValueTask<CostSpan> RunAsync(
        InstanceRef instance,
        string filePath,
        IReadOnlyList<string> args,
        ProcessMeterOptions options,
        CancellationToken ct = default);
}

/// <summary>进程计量选项（默认值即设计稿默认口径）。</summary>
public sealed record ProcessMeterOptions
{
    public TimeSpan? Timeout { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public bool CaptureOutput { get; init; } = true;
    public bool KillTreeOnTimeout { get; init; } = true;
    public MemoryBasis MemoryBasis { get; init; } = MemoryBasis.PeakXDuration;
    public DiskMeterMode DiskMode { get; init; } = DiskMeterMode.Throughput;
    public IReadOnlyList<string>? FootprintRoots { get; init; }
    public int MaxFootprintDepth { get; init; } = 4;
    public int MaxFootprintFiles { get; init; } = 100_000;
    public string? SelfReportedMarker { get; init; } = "#COST_JSON ";
    public string? TraceId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? Container { get; init; }
    public string? RoundId { get; init; }
    public string? TaskKey { get; init; }
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}
