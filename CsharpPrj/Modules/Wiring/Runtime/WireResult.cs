namespace Modules.Wiring.Runtime;

/// <summary>投递终态（受控词）。</summary>
public enum WireStatus
{
    /// <summary>原样投递。</summary>
    Delivered,

    /// <summary>判据未命中，显式跳过（记账）。</summary>
    Skipped,

    /// <summary>经 Adapter 转换后投递。</summary>
    Adapted,

    /// <summary>必填值缺失（可见化，禁静默跳过）。</summary>
    MissingRequired,
}

/// <summary>一条 wire 的投递结果（同时是账本记录的内容面）。</summary>
public readonly record struct WireResult(WireStatus Status, string WireId, int Length = 0, string? Reason = null)
{
    public bool Delivered => Status is WireStatus.Delivered or WireStatus.Adapted;

    public static WireResult Missing(string wireId, string? reason = null)
        => new(WireStatus.MissingRequired, wireId, 0, reason ?? "missing-required");
}
