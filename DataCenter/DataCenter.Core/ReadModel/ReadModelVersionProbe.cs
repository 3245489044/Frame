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
}
