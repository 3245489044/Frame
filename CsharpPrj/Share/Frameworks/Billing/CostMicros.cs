using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Frameworks.Billing;

/// <summary>
/// decimal USD ↔ micros 的单点换算（通用计费内核）。
/// <para>金额一律 <c>long micros</c>（1 USD = 1,000,000 micros）；Unknown 用 null，禁写 0。</para>
/// <para>K2 要求「分解项之和 == 总额」，逐项四舍五入会产生 ≤N micros 的残差；本类把残差补到绝对值最大的
/// 已换算分解项，并把残差值与未知项清单交给调用方写入 attributes（可审计）。</para>
/// </summary>
public static class CostMicros
{
    /// <summary>1 USD 对应的 micros 数。</summary>
    public const decimal PerUnit = 1_000_000m;

    /// <summary>单值换算（null ⇒ null；超出 long 范围 ⇒ <see cref="OverflowException"/>）。</summary>
    public static long? ToMicros(decimal? amount)
    {
        if (!amount.HasValue) return null;

        var scaled = amount.Value * PerUnit;
        if (scaled > long.MaxValue || scaled < long.MinValue)
        {
            throw new OverflowException(
                $"金额 {amount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} USD 超出 micros 可表示范围。");
        }

        return checked((long)decimal.Round(scaled, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// 总额 + 分解项换算，保证 Σ(已换算分解项) == total（存在未知项时仅对已知项守恒）。
    /// </summary>
    /// <param name="total">decimal 总额（null = Unknown）。</param>
    /// <param name="items">分解项（item, decimal amount, basis）。</param>
    /// <returns>
    /// micros 总额、micros 分解项（未知项 <c>AmountMicros=null</c>）、残差、
    /// 未能换算的分解项名（调用方须写入审计 attributes，禁静默丢失）。
    /// </returns>
    public static (long? Total, IReadOnlyList<MicrosItem> Items, long Residual, IReadOnlyList<string> UnknownItems) Map(
        decimal? total, IEnumerable<(string Item, decimal Amount, string? Basis)>? items)
    {
        if (!total.HasValue)
        {
            return (null, Array.Empty<MicrosItem>(), 0, Array.Empty<string>());
        }

        var mapped = new List<MicrosItem>();
        var unknown = new List<string>();
        foreach (var item in items ?? Enumerable.Empty<(string, decimal, string?)>())
        {
            var micros = ToMicros(item.Amount);
            if (micros is null) unknown.Add(item.Item);
            mapped.Add(new MicrosItem(item.Item, micros, item.Basis));
        }

        var totalMicros = ToMicros(total)
            ?? throw new InvalidOperationException("total 已提供但换算结果为空（不应发生）。");

        var known = mapped.Where(static i => i.AmountMicros.HasValue).ToList();
        if (known.Count == 0)
        {
            // 总额已知但没有任何可换算的分解项：不伪造 0，残差保持 0，未知项由调用方审计。
            return (totalMicros, mapped, 0, unknown);
        }

        var sum = known.Sum(static i => i.AmountMicros!.Value);
        var residual = totalMicros - sum;
        if (residual != 0)
        {
            var index = 0;
            for (var i = 1; i < known.Count; i++)
            {
                if (Math.Abs(known[i].AmountMicros!.Value) > Math.Abs(known[index].AmountMicros!.Value)) index = i;
            }

            var target = known[index];
            var adjusted = checked(target.AmountMicros!.Value + residual);
            var mappedIndex = mapped.IndexOf(target);
            mapped[mappedIndex] = target with { AmountMicros = adjusted };
        }

        return (totalMicros, mapped, residual, unknown);
    }
}

/// <summary>micros 分解项（<see cref="AmountMicros"/> 为 null = Unknown，禁写 0）。</summary>
public sealed record MicrosItem(string Item, long? AmountMicros, string? Basis);
