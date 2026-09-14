using Strategy.Observability.Envelope;
using Strategy.Observability.Query;

namespace Operation.StatisticalDriven.Pricing;

/// <summary>
/// 成本引擎：对观测事件流做<b>查询投影</b>（复用 <see cref="IObservabilityQuery.TimelineAsync"/>，单遍折叠），
/// 产出 <see cref="CostBreakdown"/>。不另起并行数据源（降级原则），不含 I/O 之外的副作用。
/// </summary>
/// <remarks>
/// 量纲约定：Metrics 值为 double；token 度量按整取（<see cref="ToLong"/>），
/// 资源度量保留 double。金额以 decimal 输出。
/// </remarks>
public sealed class CostEngine
{
    private readonly IObservabilityQuery _query;
    private readonly PricingTable _pricing;

    public CostEngine(IObservabilityQuery query, PricingTable pricing)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
    }

    /// <summary>
    /// 汇总某作用域（Run/Round/Session 均可，scope 聚簇）内 LLM + 计算 + 输出生命周期成本。
    /// </summary>
    /// <param name="scopeId">作用域键（E1/E4；如 RunId 或 SessionId 生成的 scope）。</param>
    /// <param name="provider">模型供应商；未命中时回落 <see cref="PricingTable.Default"/>。</param>
    /// <param name="model">模型名；未命中时回落 <see cref="PricingTable.Default"/>。</param>
    public async Task<CostBreakdown> ProjectAsync(
        string scopeId, string provider, string model, CancellationToken ct = default)
    {
        var pricing = _pricing.Resolve(provider, model);

        long inTokens = 0, cachedTokens = 0, outTokens = 0;
        long llmCalls = 0;
        decimal llmCost = 0m;

        double cpuSec = 0, memGbSec = 0, ioBytes = 0;
        long computeSpans = 0;

        long reinjectedTokens = 0;
        long reinjectCount = 0;

        await foreach (var evt in _query.TimelineAsync(scopeId, null, int.MaxValue, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            switch (evt.EventKind)
            {
                case CostEventKinds.LlmCallCompleted:
                {
                    var input = ToLong(Metric(evt, CostEventKinds.TokenIn));
                    var cached = ToLong(Metric(evt, CostEventKinds.TokenCached));
                    var output = ToLong(Metric(evt, CostEventKinds.TokenOut));
                    var ctx = OptionalLong(Metric(evt, CostEventKinds.ContextLength));

                    var (_, cost) = CostFormulas.LlmCallCost(input, cached, output, ctx, pricing);

                    inTokens += input;
                    cachedTokens += cached;
                    outTokens += output;
                    llmCost += cost;
                    llmCalls++;
                    break;
                }
                case CostEventKinds.ComputeSpanCompleted:
                {
                    cpuSec += Metric(evt, CostEventKinds.CpuUserSeconds)
                              + Metric(evt, CostEventKinds.CpuKernelSeconds);
                    memGbSec += Metric(evt, CostEventKinds.MemoryGbSeconds);
                    ioBytes += Metric(evt, CostEventKinds.IoReadBytes)
                               + Metric(evt, CostEventKinds.IoWriteBytes);
                    computeSpans++;
                    break;
                }
                case CostEventKinds.OutputReinjected:
                    reinjectedTokens += ToLong(Metric(evt, CostEventKinds.ReinjectedTokens));
                    reinjectCount++;
                    break;
            }
        }

        var computeCost = CostFormulas.ComputeCost(cpuSec, memGbSec, ioBytes, _pricing.Resources);
        var lifecycleCost = CostFormulas.LifecycleCost(reinjectedTokens, pricing.EffectiveInputPrice(null));

        return new CostBreakdown
        {
            LlmCallCount = llmCalls,
            ComputeSpanCount = computeSpans,
            ReinjectCount = reinjectCount,
            InputTokens = inTokens,
            CachedTokens = cachedTokens,
            OutputTokens = outTokens,
            CpuSeconds = cpuSec,
            MemoryGbSeconds = memGbSec,
            IoBytes = ioBytes,
            ReinjectedTokens = reinjectedTokens,
            LlmCost = llmCost,
            ComputeCost = computeCost,
            LifecycleCost = lifecycleCost,
        };
    }

    private static double Metric(ObservabilityEvent evt, string key)
        => evt.Metrics is { } m && m.TryGetValue(key, out var v) ? v : 0d;

    private static long? OptionalLong(double v) => v > 0d ? (long?)Math.Round(v) : null;

    private static long ToLong(double v) => (long)Math.Round(v);
}