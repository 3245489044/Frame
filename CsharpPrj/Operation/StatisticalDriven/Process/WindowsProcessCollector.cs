using System.Diagnostics;

namespace Operation.StatisticalDriven.Process;

/// <summary>
/// Windows 进程树资源采集器（一期：根进程级采样；进程树统计为后续扩展）。
/// 指标来源：CPU = TotalProcessorTime（用户+内核），内存 = 工作集周期采样积分出 GB·秒 + 峰值，
/// IO = 进程级 IO 计数器（Win 下需提升权限，一期为 0，Job Object 升级路径见设计文档 5.2.1）。
/// </summary>
public sealed class WindowsProcessCollector : IProcessCollector
{
    public async Task<ProcessMeasurement> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        using var p = new System.Diagnostics.Process { StartInfo = psi };
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();

        var sw = Stopwatch.StartNew();
        p.Start();
        var stdoutTask = p.StandardOutput.BaseStream.CopyToAsync(stdout, ct);
        var stderrTask = p.StandardError.BaseStream.CopyToAsync(stderr, ct);

        // 工作集周期采样：累计内存 GB·秒
        double gbSeconds = 0;
        var lastSample = DateTime.UtcNow;
        while (!p.HasExited)
        {
            ct.ThrowIfCancellationRequested();
            if (request.TimeoutMs > 0 && sw.ElapsedMilliseconds > request.TimeoutMs)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* 已退出 */ }
                break;
            }
            try
            {
                var now = DateTime.UtcNow;
                var seconds = (now - lastSample).TotalSeconds;
                gbSeconds += p.WorkingSet64 / 1e9 * seconds;
                lastSample = now;
            }
            catch (InvalidOperationException)
            {
                break; // 进程已退出，采样终止
            }
            await Task.Delay(request.SampleIntervalMs, ct).ConfigureAwait(false);
        }

        p.WaitForExit();
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        sw.Stop();

        var exitCode = p.HasExited ? p.ExitCode : -1;
        var outputBytes = stdout.Length + stderr.Length;

        return new ProcessMeasurement
        {
            ExitCode = exitCode,
            WallMs = sw.Elapsed.TotalMilliseconds,
            CpuUserSeconds = p.UserProcessorTime.TotalSeconds,
            CpuKernelSeconds = p.PrivilegedProcessorTime.TotalSeconds,
            PeakMemoryBytes = p.PeakWorkingSet64,
            MemoryGbSeconds = gbSeconds,
            IoReadBytes = 0,
            IoWriteBytes = 0,
            OutputBytes = outputBytes,
            OutputTokensEstimated = TokenEstimation.FromBytes(outputBytes),
            ProcessCount = 1,
        };
    }
}