using Share.Frameworks.Process;

namespace Operation.StatisticalDriven.Process;

/// <summary>
/// Windows 进程树资源采集器（一期：根进程级采样；进程树统计为后续扩展）。
/// 指标来源：CPU = 子进程 CPU 时间（用户+内核），内存 = 工作集周期采样积分出 GB·秒 + 峰值，
/// IO = 进程级 IO 计数器（Win 下需提升权限，一期为 0，Job Object 升级路径见设计文档 5.2.1）。
/// <para>执行/超时/取消/采样编排统一由 Share.Frameworks.Process.ProcessRunner 承担（不再在本类复制）。</para>
/// </summary>
public sealed class WindowsProcessCollector : IProcessCollector
{
    public async Task<ProcessMeasurement> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await ProcessRunner.RunAsync(
            new ProcessRunSpec
            {
                FileName = request.FileName,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory,
                TimeoutMs = request.TimeoutMs,
                SampleIntervalMs = request.SampleIntervalMs,
            },
            ct).ConfigureAwait(false);

        return new ProcessMeasurement
        {
            ExitCode = result.ExitCode,
            TimedOut = result.TimedOut,
            WallMs = result.WallMs,
            CpuUserSeconds = result.CpuUserSeconds,
            CpuKernelSeconds = result.CpuKernelSeconds,
            PeakMemoryBytes = result.PeakMemoryBytes,
            MemoryGbSeconds = result.MemoryGbSeconds,
            IoReadBytes = 0,
            IoWriteBytes = 0,
            OutputBytes = result.OutputBytes,
            OutputTokensEstimated = TokenEstimation.FromBytes(result.OutputBytes),
            ProcessCount = 1,
        };
    }
}
