using System;

namespace FrameworkAbstractions.ErrorHandling;

/// <summary>异常处置结果（取消/重抛/降级三态）。</summary>
public enum FailureDisposition
{
    /// <summary>取消语义：必须原样重抛（禁折成失败或部分结果）。</summary>
    Cancelled = 0,

    /// <summary>确定性失败：必须重抛（重跑必然同失败）。</summary>
    Rethrow = 1,

    /// <summary>可降级：调用方可转显式降级结果，但**必须**留痕（禁静默）。</summary>
    Degraded = 2,
}

/// <summary>处置决定（类别 + 稳定码 + 原因 + 是否必须可见化）。</summary>
public sealed record FailureDecision(
    FailureDisposition Disposition,
    string Code,
    string Reason,
    bool MustBeVisible)
{
    /// <summary>是否允许调用方继续（仅降级态）。</summary>
    public bool CanContinue => Disposition == FailureDisposition.Degraded;
}

/// <summary>失败处置码受控词（与 Agent.Contract.Outcome 码族同构，禁自由文本）。</summary>
public static class FailureCodes
{
    public const string Cancelled = "cancelled";
    public const string Timeout = "timeout";
    public const string InvalidInput = "invalid-input";
    public const string NotAssembled = "not-assembled";
    public const string Unexpected = "unexpected";
}

/// <summary>
/// 失败出口策略（通用件）：把异常收敛为三态处置，禁「catch 后继续且不留痕」。
/// <para>装配期/配置期异常（InvalidOperationException/ArgumentException）一律重抛；其余可降级但必须可见化。</para>
/// </summary>
public static class FailurePolicy
{
    /// <summary>按异常类型与阶段判定处置。</summary>
    /// <param name="ex">捕获到的异常。</param>
    /// <param name="phase">阶段名（进入码/原因文本，如 <c>harvest</c>）。</param>
    public static FailureDecision Classify(Exception ex, string phase)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var p = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase.Trim();

        if (ex is OperationCanceledException or TaskCanceledException)
            return new FailureDecision(FailureDisposition.Cancelled, $"{p}.{FailureCodes.Cancelled}", ex.Message, MustBeVisible: true);

        if (ex is TimeoutException)
            return new FailureDecision(FailureDisposition.Rethrow, $"{p}.{FailureCodes.Timeout}", ex.Message, MustBeVisible: true);

        if (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            return new FailureDecision(FailureDisposition.Rethrow, $"{p}.{FailureCodes.InvalidInput}", ex.Message, MustBeVisible: true);

        return new FailureDecision(FailureDisposition.Degraded, $"{p}.{FailureCodes.Unexpected}", ex.Message, MustBeVisible: true);
    }

    /// <summary>
    /// 统一守卫：取消/确定性失败重抛；可降级异常返回决定供调用方显式处理（调用方负责留痕）。
    /// </summary>
    public static FailureDecision Guard(Exception ex, string phase)
    {
        var decision = Classify(ex, phase);
        if (decision.Disposition != FailureDisposition.Degraded) throw ex;
        return decision;
    }

    /// <summary>是否取消语义（含被包装的取消异常）。</summary>
    public static bool IsCancellation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or TaskCanceledException) return true;
        }

        return false;
    }
}
