using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Modules.Threading;

namespace Share.Frameworks.Process;

/// <summary>
/// 子进程执行编排器：启动 → stdout/stderr 并行读取 → 超时/取消经 <see cref="IProcessHandle"/> 杀树 → 周期资源采样。
/// <para>
/// 纪律：
/// ① 取消与超时都必须先杀整棵进程树，避免孤儿进程；
/// ② 墙钟与内存积分共用同一 <see cref="Stopwatch"/>（单调时钟），不混用 <see cref="DateTime.UtcNow"/>；
/// ③ 采样间隔夹取到 [1, 60000] 毫秒，杜绝 0 间隔忙循环。
/// </para>
/// </summary>
public static class ProcessRunner
{
    private static readonly TimeSpan StreamDrainGrace = TimeSpan.FromSeconds(10);

    /// <summary>执行一次子进程并按预算采样资源。取消时先杀树再抛 <see cref="OperationCanceledException"/>。</summary>
    public static async Task<ProcessRunResult> RunAsync(ProcessRunSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.FileName);

        var intervalMs = Math.Clamp(spec.SampleIntervalMs <= 0 ? 1 : spec.SampleIntervalMs, 1, 60_000);

        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            // 编码单源：显式 UTF-8（不设 ⇒ 回退 Console.OutputEncoding，非 65001 下乱码/丢字，
            // 且 StdoutBytes 的 UTF-8 字节计数口径会与实际解码不一致）。
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory))
            psi.WorkingDirectory = spec.WorkingDirectory;

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        var wall = Stopwatch.StartNew();
        process.Start();

        // 终止原语唯一来源：Modules.Threading.IProcessHandle（默认杀整棵树）。
        using var handle = new ChildProcessHandle(process);
        using var killOnCancel = ct.Register(() => handle.Kill(entireProcessTree: true));

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        double gbSeconds = 0;
        long lastSampleMs = 0;
        var timedOut = false;

        try
        {
            while (SafeHasExited(process) == false)
            {
                ct.ThrowIfCancellationRequested();

                if (spec.TimeoutMs > 0 && wall.ElapsedMilliseconds >= spec.TimeoutMs)
                {
                    timedOut = true;
                    handle.Kill(entireProcessTree: true);
                    break;
                }

                var nowMs = wall.ElapsedMilliseconds;
                var seconds = Math.Max(0, nowMs - lastSampleMs) / 1000.0;
                lastSampleMs = nowMs;
                if (seconds > 0)
                {
                    try { gbSeconds += process.WorkingSet64 / 1e9 * seconds; }
                    catch (InvalidOperationException) { break; }   // 已退出：采样终止
                    catch (Win32Exception) { break; }
                }

                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            handle.Kill(entireProcessTree: true);
            throw;
        }

        // 退出等待有界：Kill 失败/进程不可杀时不永久挂死（E-8）。
        if (timedOut || SafeHasExited(process) == false)
        {
            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(StreamDrainGrace, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                handle.Kill(entireProcessTree: true);
            }
        }

        string stdout;
        string stderr;
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(StreamDrainGrace, ct).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            handle.Kill(entireProcessTree: true);
            stdout = string.Empty;
            stderr = string.Empty;
        }

        wall.Stop();

        var exited = SafeHasExited(process);
        return new ProcessRunResult
        {
            ExitCode = exited ? SafeExitCode(process) : -1,
            TimedOut = timedOut || !exited,
            WallMs = wall.Elapsed.TotalMilliseconds,
            CpuUserSeconds = exited ? SafeTime(() => process.UserProcessorTime) : 0,
            CpuKernelSeconds = exited ? SafeTime(() => process.PrivilegedProcessorTime) : 0,
            PeakMemoryBytes = exited ? SafeLong(() => process.PeakWorkingSet64) : 0,
            MemoryGbSeconds = gbSeconds,
            StandardOutput = stdout,
            StandardError = stderr,
            StdoutBytes = System.Text.Encoding.UTF8.GetByteCount(stdout),
            StderrBytes = System.Text.Encoding.UTF8.GetByteCount(stderr),
        };
    }

    private static bool SafeHasExited(System.Diagnostics.Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return true; }
    }

    private static int SafeExitCode(System.Diagnostics.Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
        catch (Win32Exception) { return -1; }
    }

    private static double SafeTime(Func<TimeSpan> read)
    {
        try { return read().TotalSeconds; }
        catch (InvalidOperationException) { return 0; }
        catch (Win32Exception) { return 0; }
    }

    private static long SafeLong(Func<long> read)
    {
        try { return read(); }
        catch (InvalidOperationException) { return 0; }
        catch (Win32Exception) { return 0; }
    }
}
