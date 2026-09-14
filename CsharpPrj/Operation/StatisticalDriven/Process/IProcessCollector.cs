namespace Operation.StatisticalDriven.Process;

/// <summary>进程树资源采集器端口：产出 <see cref="ProcessMeasurement"/>，不直接发事件（发事件见 Observe/ComputeSpanRecorder）。</summary>
public interface IProcessCollector
{
    Task<ProcessMeasurement> RunAsync(ProcessRunRequest request, CancellationToken ct = default);
}