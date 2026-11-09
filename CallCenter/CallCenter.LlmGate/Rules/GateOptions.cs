namespace CallCenter.LlmGate.Rules;

/// <summary>门配置（阈值单源；落 <c>Config/llm-gate/gate.json</c>，代码内不散落常量）。</summary>
public sealed record GateOptions
{
    public string Mode { get; init; } = "enforce";
    public string Listen { get; init; } = "http://127.0.0.1:8790";
    public string Upstream { get; init; } = "https://api.deepseek.com";

    public int MaxHop { get; init; } = 8;
    public int LoopRepeat { get; init; } = 6;
    public int LoopWindowSec { get; init; } = 60;
    public int CallsPerMinute { get; init; } = 60;
    public decimal SessionCostUsd { get; init; } = 1.00m;
    public decimal TaskCostUsd { get; init; } = 0.50m;
    public decimal DailyCostUsd { get; init; } = 10.00m;
    public long DailyTokens { get; init; } = 5_000_000;
    public decimal HoldCostUsd { get; init; } = 0.02m;
    public bool HoldUnknownModel { get; init; } = true;

    public int HoldTimeoutMs { get; init; } = 300_000;
    public int MaxPending { get; init; } = 32;
    public int RingSize { get; init; } = 2000;
    /// <summary>上游请求超时（秒）；0/负 ⇒ 不限。超时 → 504 LG_UPSTREAM_TIMEOUT。</summary>
    public int UpstreamTimeoutSec { get; init; } = 120;
    public bool StoreBody { get; init; }
    public int PreviewChars { get; init; } = 200;
    public string? DataRoot { get; init; }
    public string? ConfigPath { get; init; }
}
