using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>成本账写入端口：宿主账本适配实现（append-only，幂等键去重）。</summary>
public interface IConcurrencyCostSink
{
    ValueTask<bool> AppendAsync(ConcurrencyCostSpan span, CancellationToken ct = default);
}

/// <summary>用量查询端口：宿主成本查询适配实现（未知用量返回 (0,0)）。</summary>
public interface IConcurrencyUsageQuery
{
    Task<(long TokenIn, long TokenOut)> GetUsageAsync(string taskKey, CancellationToken ct = default);
}
