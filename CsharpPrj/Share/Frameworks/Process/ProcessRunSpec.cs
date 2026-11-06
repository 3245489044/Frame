namespace Share.Frameworks.Process;

/// <summary>一次子进程执行请求（平台无关编排参数）。</summary>
public sealed record ProcessRunSpec
{
    public required string FileName { get; init; }

    public string Arguments { get; init; } = "";

    /// <summary>工作目录；空串 = 继承当前目录。</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>墙钟超时（毫秒）；0 = 不超时。超时经 <see cref="Modules.Threading.IProcessHandle"/> 杀整棵进程树。</summary>
    public int TimeoutMs { get; init; }

    /// <summary>资源采样间隔（毫秒）；内部夹取到 [1, 60000]。</summary>
    public int SampleIntervalMs { get; init; } = 100;
}

/// <summary>子进程执行结果（stdout/stderr 已并行读完；取消时抛出 <see cref="OperationCanceledException"/>）。</summary>
public sealed record ProcessRunResult
{
    public required int ExitCode { get; init; }

    /// <summary>是否因超时被杀树（true 时 ExitCode 不具业务意义）。</summary>
    public required bool TimedOut { get; init; }

    /// <summary>墙钟耗时（毫秒，单调时钟）。</summary>
    public required double WallMs { get; init; }

    public double CpuUserSeconds { get; init; }

    public double CpuKernelSeconds { get; init; }

    /// <summary>峰值工作集（字节）。</summary>
    public long PeakMemoryBytes { get; init; }

    /// <summary>累计内存占用（GB·秒，单调时钟采样积分）。</summary>
    public double MemoryGbSeconds { get; init; }

    public string StandardOutput { get; init; } = "";

    public string StandardError { get; init; } = "";

    public long StdoutBytes { get; init; }

    public long StderrBytes { get; init; }

    /// <summary>stdout+stderr 字节数。</summary>
    public long OutputBytes => StdoutBytes + StderrBytes;
}
