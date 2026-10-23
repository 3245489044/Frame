using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.StateManagement.Audit;

/// <summary>
/// 状态根取样（只读）：一次「取根」的结构事实。
/// <para>判据载体 = (RootPath, IdentityHash, ContentHash) 三元组；不含执行者叙述。</para>
/// <para>
/// IdentityHash = 根承载者身份指纹：进程内可取 <c>RuntimeHelpers.GetHashCode(root)</c>（同对象 ⇒ 同值）；
/// 跨进程（headless 诊断）取「解析后规范化路径」的稳定指纹。ContentHash = 根内数据库文件内容 SHA256。
/// </para>
/// </summary>
/// <param name="RootPath">逻辑根的解析结果（绝对路径）。</param>
/// <param name="IdentityHash">根承载者身份指纹（进程内对象 identity 或跨进程路径指纹）。</param>
/// <param name="ContentHash">根内容指纹；空 = 无可审计内容（未落库/根为空）。</param>
/// <param name="AtUtc">取样时刻（由调用方给，取样器不读时钟可变源）。</param>
public readonly record struct StateRootSample(
    string RootPath,
    string IdentityHash,
    string ContentHash,
    DateTimeOffset AtUtc);

/// <summary>状态根裁定三态：<see cref="Unknown"/> 为「样本不足/不可判定」，不得冒充 Single。</summary>
public enum StateRootVerdict
{
    /// <summary>不可判定（样本不足 / 根未解析 / 根为空）。</summary>
    Unknown,

    /// <summary>两次取根命中同一承载者且内容一致。</summary>
    Single,

    /// <summary>两份快照（路径/身份/内容任一不一致）。</summary>
    Split,
}

/// <summary>
/// 状态根唯一性判据（纯函数）：把「根是否单实例」做成常驻、可复算的事实，取代一次性取证脚本。
/// <para>
/// 口径：<b>不可判定 ≠ 单实例</b>（<c>AX.NoSilentOutlet</c>）。样本不足、根未解析、根为空一律 <see cref="StateRootVerdict.Unknown"/>。
/// </para>
/// </summary>
public static class StateRootAudit
{
    /// <summary>理由码（诊断输出单源）。</summary>
    public const string ReasonInsufficientSamples = "insufficient-samples";
    public const string ReasonRootUnresolved = "root-unresolved";
    public const string ReasonRootEmpty = "root-empty";
    public const string ReasonPathMismatch = "path-mismatch";
    public const string ReasonIdentityMismatch = "identity-mismatch";
    public const string ReasonContentMismatch = "content-mismatch";
    public const string ReasonSingleRoot = "single-root";

    /// <summary>裁定根唯一性。<paramref name="reason"/> 给出结构理由码（永不为空）。</summary>
    public static StateRootVerdict Inspect(IReadOnlyList<StateRootSample>? samples, out string reason)
    {
        if (samples is null || samples.Count < 2)
        {
            reason = ReasonInsufficientSamples;
            return StateRootVerdict.Unknown;
        }

        if (samples.Any(static s => string.IsNullOrWhiteSpace(s.RootPath)))
        {
            reason = ReasonRootUnresolved;
            return StateRootVerdict.Unknown;
        }

        if (samples.Any(static s => string.IsNullOrWhiteSpace(s.ContentHash)))
        {
            reason = ReasonRootEmpty;
            return StateRootVerdict.Unknown;
        }

        if (Distinct(samples, static s => s.RootPath))
        {
            reason = ReasonPathMismatch;
            return StateRootVerdict.Split;
        }

        if (Distinct(samples, static s => s.IdentityHash))
        {
            reason = ReasonIdentityMismatch;
            return StateRootVerdict.Split;
        }

        if (Distinct(samples, static s => s.ContentHash))
        {
            reason = ReasonContentMismatch;
            return StateRootVerdict.Split;
        }

        reason = ReasonSingleRoot;
        return StateRootVerdict.Single;
    }

    /// <summary>两样本便捷重载（诊断入口/单调用点）：等同 <c>Inspect(new[]{first, second})</c>。</summary>
    public static StateRootVerdict Inspect(
        StateRootSample first, StateRootSample second, out string reason)
        => Inspect(new[] { first, second }, out reason);

    /// <summary>诊断码（报告面单源）：<c>state.root-single</c> / <c>state.root-split</c> / <c>state.root-unknown</c>。</summary>
    public static string CodeOf(StateRootVerdict verdict) => verdict switch
    {
        StateRootVerdict.Single => "state.root-single",
        StateRootVerdict.Split => "state.root-split",
        _ => "state.root-unknown",
    };

    private static bool Distinct(
        IReadOnlyList<StateRootSample> samples, Func<StateRootSample, string?> selector)
        => samples.Select(selector).Distinct(StringComparer.Ordinal).Count() > 1;
}
