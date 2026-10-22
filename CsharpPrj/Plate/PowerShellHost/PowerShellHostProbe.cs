using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.PowerShellHost;

/// <summary>探测结果（版本 / 退出码 / 双流原文 / 耗时）。</summary>
public sealed record PowerShellProbeResult(
    string ExecutablePath,
    string Version,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Elapsed);

/// <summary>
/// PowerShell 宿主可执行探测（全仓单一实现）：<c>--version</c> 有界探测。
/// <para>
/// 判据载体 = 结构事实（退出码 / 版本行 / 耗时）。双流**并发排空**后再等待退出，避免缓冲区写满互等；
/// 超时杀进程树并以稳定码失败（禁静默回退）。定位统一委托 <see cref="PowerShellHostLocator"/>。
/// </para>
/// </summary>
public static class PowerShellHostProbe
{
    /// <summary>默认探测超时（与历史 <c>ProbeOrFail</c> 一致）。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex VersionPattern = new(
        @"^PowerShell\s+(\d+(?:\.\d+){1,3})",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 探测可执行（默认 <c>--version</c>）。失败抛 <see cref="PowerShellHostProbeException"/>。
    /// </summary>
    /// <param name="executablePath">显式路径；null/空 ⇒ 先经 <see cref="PowerShellHostLocator.Locate"/> 定位。</param>
    /// <param name="timeout">探测上限（默认 <see cref="DefaultTimeout"/>）。</param>
    /// <param name="allowWindowsPowerShell51">定位时是否允许回落到 Windows PowerShell 5.1。</param>
    /// <param name="detailSink">定位候选明细出口（null = 丢弃）。</param>
    /// <param name="arguments">探测参数（默认 <c>--version</c>；测试/诊断可替换）。</param>
    public static async Task<PowerShellProbeResult> ProbeAsync(
        string? executablePath = null,
        TimeSpan? timeout = null,
        bool allowWindowsPowerShell51 = false,
        Action<string>? detailSink = null,
        string arguments = "--version",
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        var path = string.IsNullOrWhiteSpace(executablePath)
            ? PowerShellHostLocator.Locate(null, allowWindowsPowerShell51, detailSink)
            : executablePath!;

        if (!File.Exists(path))
        {
            throw new PowerShellHostProbeException(
                PowerShellHostCodes.ExecutableMissing,
                $"可执行文件不存在：{path}",
                path);
        }

        var limit = timeout ?? DefaultTimeout;
        var effectiveWorkingDirectory = ResolveWorkingDirectory(workingDirectory);
        var effectiveArguments = ResolveArguments(path, arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = effectiveArguments,
            WorkingDirectory = effectiveWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // GUI 宿主没有控制台：不重定向 stdin 会继承无效句柄（WinExe 下 CreateProcess 可能 ACCESS_DENIED）。
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        PowerShellHostEnvironment.Apply(startInfo);
        // 探测只依赖系统目录；隔离宿主 PATH 污染（某些宿主 PATH 含不可访问目录 ⇒ CreateProcess ACCESS_DENIED）。
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
            startInfo.Environment["PATH"] = Path.Combine(windows, "System32") + ";" + windows;

        using var process = new Process { StartInfo = startInfo };
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new PowerShellHostProbeException(
                    PowerShellHostCodes.ProbeFailed,
                    $"可执行文件启动失败：{path}（cwd={effectiveWorkingDirectory}）",
                    path,
                    workingDirectory: effectiveWorkingDirectory);
            }

            // 探测不需要输入；关闭 stdin 避免子进程等待。
            try { process.StandardInput.Close(); } catch { }
        }
        catch (PowerShellHostProbeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var nativeError = (ex as Win32Exception)?.NativeErrorCode;
            var suffix = nativeError is null ? string.Empty : $" win32={nativeError}";
            var diag = TryDiagnosticStart(effectiveWorkingDirectory);
            throw new PowerShellHostProbeException(
                PowerShellHostCodes.ProbeFailed,
                $"可执行文件启动失败（{ex.GetType().Name}{suffix} diag={diag}）：{path}（cwd={effectiveWorkingDirectory}）{ex.Message}",
                path,
                inner: ex,
                nativeErrorCode: nativeError,
                workingDirectory: effectiveWorkingDirectory);
        }

        // 先起排空（并发读双流），再等待退出：输出量大时不会与子进程互等。
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (limit > TimeSpan.Zero) timeoutCts.CancelAfter(limit);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            elapsed.Stop();
            var tail = Tail(await SafeAwait(stderrTask).ConfigureAwait(false));
            throw new PowerShellHostProbeException(
                PowerShellHostCodes.ProbeTimeout,
                $"可执行探测超时（>={limit.TotalSeconds:0.##}s）：{path}",
                path,
                stderrTail: tail);
        }

        elapsed.Stop();
        var stdout = await SafeAwait(stdoutTask).ConfigureAwait(false);
        var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new PowerShellHostProbeException(
                PowerShellHostCodes.ProbeFailed,
                $"可执行探测失败（exit={process.ExitCode}）：{path} {stderr}".Trim(),
                path,
                exitCode: process.ExitCode,
                stderrTail: Tail(stderr));
        }

        var match = VersionPattern.Match(stdout);
        var version = match.Success ? match.Groups[1].Value : string.Empty;

        return new PowerShellProbeResult(path, version, process.ExitCode, stdout, stderr, elapsed.Elapsed);
    }

    private static string ResolveArguments(string path, string arguments)
    {
        if (!string.Equals(Path.GetFileName(path), "powershell.exe", StringComparison.OrdinalIgnoreCase))
            return arguments;

        // Windows PowerShell 5.1 不认 --version；用命令行取版本。
        return string.Equals(arguments, "--version", StringComparison.Ordinal)
            ? "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\""
            : arguments;
    }

    private static string TryDiagnosticStart(string workingDirectory)
    {
        try
        {
            var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = "/c exit 0",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            PowerShellHostEnvironment.Apply(psi);
            using var process = Process.Start(psi);
            if (process is null) return "cmd-null";
            try { process.StandardInput.Close(); } catch { }
            process.WaitForExit(2000);
            return "cmd-exit=" + process.ExitCode;
        }
        catch (Exception ex)
        {
            var native = (ex as Win32Exception)?.NativeErrorCode;
            return "cmd-" + (native?.ToString() ?? ex.GetType().Name);
        }
    }

    private static string ResolveWorkingDirectory(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Directory.Exists(requested))
            return requested!;

        var current = Environment.CurrentDirectory;
        return !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
            ? current
            : AppContext.BaseDirectory;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 退出路径不抛（进程可能已自然结束）。
        }
    }

    private static async Task<string> SafeAwait(Task<string> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch
        {
            // 超时杀进程后流被关闭：返回已读部分（诊断面不抛）。
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> Tail(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return Array.Empty<string>();
        var lines = stderr.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = Math.Max(0, lines.Length - 16);
        var tail = new string[lines.Length - start];
        Array.Copy(lines, start, tail, 0, tail.Length);
        return tail;
    }
}
