using System.Collections.Generic;

namespace Modules.Node.Stream;

/// <summary>
/// 聚合容器的**丢失账**（限流丢弃 + 未成轮拒收）—— Stream 形态域唯一的有损事实读数面。
/// <para>语义：<paramref name="Dropped"/> = run 已建立但被限流（同 kind 连续同载荷 / 超上限）；
/// <paramref name="Refused"/> = run 边界未建立（未 BeginRound / 已 Release）时的拒收，二者不可混同。</para>
/// </summary>
/// <param name="Dropped">按类别统计的被丢弃条数。</param>
/// <param name="Refused">按类别统计的被拒收条数。</param>
/// <param name="DroppedTotal">被丢弃总条数。</param>
/// <param name="RefusedTotal">被拒收总条数。</param>
public sealed record AggregateAccounting(
    IReadOnlyDictionary<string, int> Dropped,
    IReadOnlyDictionary<string, int> Refused,
    long DroppedTotal,
    long RefusedTotal)
{
    /// <summary>是否存在有损事实（false ⇒ 无需可见化，调用方据此免打日志）。</summary>
    public bool HasLoss => DroppedTotal > 0 || RefusedTotal > 0;
}
