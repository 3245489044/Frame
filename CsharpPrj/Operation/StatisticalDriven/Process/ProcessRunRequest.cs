namespace Operation.StatisticalDriven.Process;

/// <summary>一次进程树执行请求。</summary>
public sealed record ProcessRunRequest
{
    public required string FileName { get; init; }

    public string Arguments { get; init; } = "";

    public string WorkingDirectory { get; init; } = "";

    /// <summary>工作集采样间隔（毫秒），用于累计内存 GB·秒。</summary>
    public int SampleIntervalMs { get; init; } = 100;

    /// <summary>超时（毫秒）；0 = 不超时。超时则终止整棵进程树。</summary>
    public int TimeoutMs { get; init; } = 0;
}