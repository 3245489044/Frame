using Operation.StatisticalDriven.Pricing;
using Operation.StatisticalDriven.Process;
using Strategy.Observability.Bus;
using Strategy.Observability.Entity;

namespace Operation.StatisticalDriven.Observe;

/// <summary>
/// 计算 Span 录制器：把一次 <see cref="IProcessCollector"/> 执行录制成标准信封事件
/// （ComputeSpanStarted → 采集 → ComputeSpanCompleted，经 <see cref="Activity"/> 幂等补全）。
/// 采集与事件化分离：本类只做编排，不含采集细节。
/// </summary>
public sealed class ComputeSpanRecorder
{
    private readonly IObservabilityBus _bus;
    private readonly IProcessCollector _collector;
    private readonly EntityIdFactory _ids;

    public ComputeSpanRecorder(IObservabilityBus bus, IProcessCollector collector, EntityIdFactory ids)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    /// <summary>
    /// 在 <paramref name="scopeId"/> 作用域下执行请求并录制为 ComputeSpan 事件对；返回测量结果。
    /// </summary>
    /// <param name="scopeId">作用域键（E1/E4；如 RunId 或 RoundId 生成的 scope）。</param>
    public async Task<ProcessMeasurement> RunAsync(string scopeId, ProcessRunRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(request);

        var entity = _ids.New(scopeId, CostEventKinds.EntityCompute);

        // 起始事件需显式 Emit（Activity 构造只持有句柄，起始事件由 Obs.Begin 负责；此处直接用内核总线）
        _bus.Emit(entity, CostEventKinds.ComputeSpanStarted);
        using var activity = new Activity(
            _bus, entity, CostEventKinds.ComputeSpanStarted, CostEventKinds.ComputeSpanCompleted);

        var m = await _collector.RunAsync(request, ct).ConfigureAwait(false);

        activity.Complete(b => b
            .WithMetric(CostEventKinds.CpuUserSeconds, m.CpuUserSeconds)
            .WithMetric(CostEventKinds.CpuKernelSeconds, m.CpuKernelSeconds)
            .WithMetric(CostEventKinds.MemoryGbSeconds, m.MemoryGbSeconds)
            .WithMetric(CostEventKinds.IoReadBytes, m.IoReadBytes)
            .WithMetric(CostEventKinds.IoWriteBytes, m.IoWriteBytes)
            .WithMetric(CostEventKinds.OutputBytes, m.OutputBytes)
            .WithMetric(CostEventKinds.OutputTokensEstimated, m.OutputTokensEstimated)
            .WithMetric(CostEventKinds.ExitCode, m.ExitCode)
            .Payload(new
            {
                request.FileName,
                request.Arguments,
                request.WorkingDirectory,
                m.ExitCode,
                m.WallMs,
            }));

        return m;
    }
}