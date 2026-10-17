using CallCenter.Cost.Metering;

namespace CallCenter.Cost.Ledger;

/// <summary>内存落库实现（测试 / 默认禁用态；生产由 Agent.Core.Cost 适配节点写入）。</summary>
public sealed class InMemoryCostSink : ICostSink
{
    private readonly CostLedger _ledger;

    public InMemoryCostSink(CostLedger? ledger = null) => _ledger = ledger ?? new CostLedger();

    public CostLedger Ledger => _ledger;

    public ValueTask WriteAsync(IReadOnlyList<CostSpan> spans, CancellationToken ct = default)
    {
        if (spans is not null)
        {
            _ledger.AddRange(spans);
        }

        return ValueTask.CompletedTask;
    }
}
