using System;
using System.Collections.Generic;

namespace Modules.Node.Stream;

/// <summary>
/// 迟到帧残渣（**载荷保留**，非只计数）—— 轮已结算 / 未成轮期间到达的帧在落库侧的可见面。
/// <para>
/// 谁写入：<see cref="AggregateRun.Append"/> 的拒收路径；谁清理：<see cref="AggregateRun.Release"/>（先报后清）。
/// 进程退出：随内存丢失（残渣属诊断事实，不持久化；丢失本身由拒收计数可见化）。
/// </para>
/// </summary>
/// <param name="RunId">所属 run（未成轮 ⇒ null）。</param>
/// <param name="LastRound">被拒收时的当前轮次（-1 = 轮已结算 / 未建立）。</param>
/// <param name="RefusedTotal">拒收总条数。</param>
/// <param name="RefusedOfKind">按 kind 的拒收条数。</param>
/// <param name="Samples">有界载荷样本（首 N 条，超出淘汰最旧）。</param>
/// <param name="LastRefused">最后一条拒收帧的完整载荷（有界截断）。</param>
/// <param name="FirstRefusedAtUtc">首条拒收到达时刻。</param>
/// <param name="LastRefusedAtUtc">最后一条拒收到达时刻。</param>
public sealed record AggregateResidue(
    string? RunId,
    int LastRound,
    long RefusedTotal,
    IReadOnlyDictionary<string, int> RefusedOfKind,
    IReadOnlyList<ResidueSample> Samples,
    ResidueSample? LastRefused,
    DateTimeOffset? FirstRefusedAtUtc,
    DateTimeOffset? LastRefusedAtUtc)
{
    /// <summary>是否存在残渣（false ⇒ 无需上报）。</summary>
    public bool HasResidue => RefusedTotal > 0;

    /// <summary>空残渣。</summary>
    public static AggregateResidue None { get; } = new(
        null, -1, 0, new Dictionary<string, int>(0), Array.Empty<ResidueSample>(), null, null, null);
}

/// <summary>
/// 一条残渣样本（帧载荷原文，已按上限截断）。
/// </summary>
/// <param name="Kind">帧类别。</param>
/// <param name="Payload">帧载荷 JSON（含 <c>refused:true</c>；超限截断）。</param>
/// <param name="AtUtc">到达时刻。</param>
public sealed record ResidueSample(string Kind, string Payload, DateTimeOffset AtUtc);
