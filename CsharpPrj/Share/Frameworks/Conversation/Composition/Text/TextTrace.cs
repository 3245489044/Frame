#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>
/// 装配观测 —— 一次装配的结构化事实（键/状态/哈希/计数，<b>不含正文</b>）。
/// </summary>
/// <param name="ProfileKey">档案键。</param>
/// <param name="PlanFingerprint">计划指纹。</param>
/// <param name="AssemblyFingerprint">装配指纹。</param>
/// <param name="Verdict">判定标签。</param>
/// <param name="SegmentCount">段数。</param>
/// <param name="Missing">缺失键。</param>
/// <param name="Unknown">未知键。</param>
/// <param name="NotApplicable">不适用键。</param>
/// <param name="Origin">来源标记。</param>
public sealed record TextTrace(
    string ProfileKey,
    string PlanFingerprint,
    string AssemblyFingerprint,
    string Verdict,
    int SegmentCount,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<string> NotApplicable,
    string Origin)
{
    /// <summary>由计划/装配/结论构建观测（成功与失败都发一条）。</summary>
    public static TextTrace Of(
        TextPlan plan,
        TextAssembly assembly,
        TextVerdict? verdict,
        SnapshotOrigin origin = SnapshotOrigin.Live)
        => new(
            plan.ProfileKey,
            plan.Fingerprint,
            assembly.Fingerprint,
            (verdict?.Status ?? TextVerdictStatus.Complete).ToString(),
            plan.Blocks.Count,
            verdict?.Missing.Select(static k => k.Value).ToArray() ?? Array.Empty<string>(),
            verdict?.Unknown.Select(static k => k.Value).ToArray() ?? Array.Empty<string>(),
            verdict?.NotApplicable.Select(static k => k.Value).ToArray() ?? Array.Empty<string>(),
            origin.ToString());
}
