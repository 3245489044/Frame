#nullable enable

namespace UiBackend.Html;

/// <summary>
/// 写回运行时开关与帧预算（B5-1：L3 配置级，运行时可变）。
/// <para>默认值语义：<see cref="FrameBudget"/>=false ⇒ 只记账不丢弃（零回归）；
/// <see cref="InteractiveFastPath"/>=true ⇒ 交互来源不被收敛屏障拖慢（B4 验收所需）；
/// 其余开关默认开启（已是现状契约）。</para>
/// </summary>
public sealed record UiWriteRuntimeOptions
{
    /// <summary>缺省配置（宿主未注入时使用；单一默认源）。</summary>
    public static UiWriteRuntimeOptions Default { get; } = new();

    /// <summary>帧预算开关（关 ⇒ 只记账不丢弃）。</summary>
    public bool FrameBudget { get; init; }

    /// <summary>交互快路径开关（关 ⇒ 交互来源按刷新语义处理）。</summary>
    public bool InteractiveFastPath { get; init; } = true;

    /// <summary>单次刷新计划的操作数上限（预算判据）。</summary>
    public int MaxOpsPerFlush { get; init; } = 256;

    /// <summary>单次刷新计划的字节上限（预算判据）。</summary>
    public int MaxBytesPerFlush { get; init; } = 256 * 1024;

    /// <summary>单次刷新的落地耗时上限（毫秒；超限 ⇒ 记账并降级）。</summary>
    public int MaxMsPerFlush { get; init; } = 50;

    /// <summary>指标上报间隔（毫秒；60s 限流，防指标自身洪泛）。</summary>
    public int MetricsReportIntervalMs { get; init; } = 60_000;
}
