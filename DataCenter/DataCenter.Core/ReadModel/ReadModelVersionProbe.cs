#nullable enable

using System;
using DataCenter.Protocol.Projection;

namespace DataCenter.Core.ReadModel;

/// <summary>
/// 版本源 → 版本值（唯一映射点）：<c>None/Ttl</c> 恒 0（缓存存活期由 TTL 决定），
/// <c>Event/GraphMembership</c> 走注册探针（缺失由注册期拦截）。
/// </summary>
public static class ReadModelVersionProbe
{
    /// <summary>取本拍版本。</summary>
    /// <param name="spec">读模型声明。</param>
    /// <param name="probe">注册的版本探针（None/Ttl 可为 null）。</param>
    /// <param name="owner">归属（探针按 owner 取值）。</param>
    public static long Resolve(ReadModelSpec spec, Func<object, long>? probe, object owner)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return spec.Version switch
        {
            ReadModelVersionMode.None => 0,
            ReadModelVersionMode.Ttl => 0,
            _ => probe is null
                ? throw new InvalidOperationException($"读模型 '{spec.ModelId}' 缺版本探针。")
                : probe(owner),
        };
    }

    /// <summary>
    /// <see cref="ReadModelVersionMode.Declared"/> 的通用探针（声明指纹 → 版本）。
    /// <para>
    /// 纪律：① 指纹取不到（null/空）⇒ 版本 0（缓存不失效是显式可见的降级，由装配侧记账）；
    /// ② 同一指纹必得同一版本（FNV-1a 64 内容散列，无时间/随机分量）。
    /// </para>
    /// </summary>
    /// <param name="fingerprint">声明指纹取值器（每次读都取当前声明指纹）。</param>
    public static Func<object, long> FromFingerprint(Func<string?> fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return _ =>
        {
            var text = fingerprint();
            if (string.IsNullOrWhiteSpace(text)) return 0;

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= prime;
            }

            return unchecked((long)(hash & 0x7FFF_FFFF_FFFF_FFFFUL));
        };
    }
}
