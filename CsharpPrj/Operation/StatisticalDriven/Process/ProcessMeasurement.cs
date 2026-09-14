namespace Operation.StatisticalDriven.Process;

/// <summary>一次进程树执行的资源测量结果（设计文档 4.x compute_spans 的字段子集）。</summary>
public sealed record ProcessMeasurement
{
    public required int ExitCode { get; init; }

    /// <summary>墙钟耗时（毫秒）。</summary>
    public required double WallMs { get; init; }

    public double CpuUserSeconds { get; init; }

    public double CpuKernelSeconds { get; init; }

    /// <summary>峰值工作集（字节）。</summary>
    public long PeakMemoryBytes { get; init; }

    /// <summary>累计内存占用（GB·秒，采样积分）。</summary>
    public double MemoryGbSeconds { get; init; }

    public long IoReadBytes { get; init; }

    public long IoWriteBytes { get; init; }

    /// <summary>stdout+stderr 字节数。</summary>
    public long OutputBytes { get; init; }

    /// <summary>输出 Token 估算（见 <see cref="TokenEstimation"/>）。</summary>
    public long OutputTokensEstimated { get; init; }

    /// <summary>进程树进程数（一期：1 = 仅根进程；树计数为后续扩展）。</summary>
    public int ProcessCount { get; init; } = 1;
}