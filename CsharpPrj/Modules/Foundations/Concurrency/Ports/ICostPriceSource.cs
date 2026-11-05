using System;

namespace Modules.Concurrency;

/// <summary>单位价格（中性形态；宿主价格表映射）。</summary>
public readonly record struct CostQuote(
    string Currency,
    decimal InputPerToken,
    decimal OutputPerToken,
    decimal? CachedInputPerToken);

/// <summary>价格源端口：宿主注入；缺价返回 false（Unknown，禁以 0 冒充）。</summary>
public interface ICostPriceSource
{
    bool TryGet(string provider, string model, DateOnly date, out CostQuote quote);
}
