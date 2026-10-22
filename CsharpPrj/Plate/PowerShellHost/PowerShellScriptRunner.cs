using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plate.PowerShellHost;

/// <summary>一次性脚本执行结果（结构化；超时与退出码不混淆）。</summary>
public sealed record PowerShellScriptResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Elapsed)
{
    /// <summary>成功判据 = 未超时 ∧ exit=0（结构事实）。</summary>
    public bool Ok => !TimedOut && ExitCode == 0;
}

/// <summary>一次性脚本执行选项（默认值 = 现有产代构建的实际行为）。</summary>
public sealed record PowerShellScriptOptions
{
    public string? WorkingDirectory { get; init; }

    /// <summary>null = 无上限（与历史 <c>WaitForExit()</c> 等价；调用方应显式给值）。</summary>
    public TimeSpan? Timeout { get; init; }

    public bool NoProfile { get; init; } = true;

    /// <summary>默认 false：保持历史 invocation 语义（未加此参数），需要时由调用方显式开启。</summary>
    public bool NonInteractive { get; init; }

    public bool BypassExecutionPolicy { get; init; } = true;

    /// <summary>是否允许回落到 Windows PowerShell 5.1（默认 false，与契约一致）。</summary>
    public bool AllowWindowsPowerShell51 { get; init; }

    /// <summary>附加/覆盖环境变量（null = 继承父进程）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>已解析的真实 pwsh 路径（由 PowerShellHostRuntime 提供；空则自行定位）。</summary>
    public string? ResolvedExecutable { get; init; }
}

/// <summary>
/// PowerShell 一次性脚本执行（全仓单一实现）：定位 → <c>-File</c> 启动 → 并发排空双流 → 超时杀进程树。
/// <para>
/// 判据载体 = 结构事实（退出码 / stdout / stderr / 超时标志）。禁止调用方各自拼
/// <c>ProcessStartInfo</c> + <c>-File</c>（门禁 [A6]/[A7]）。脚本不存在即 fail-fast（禁静默当成功）。
/// </para>
/// </summary>
public static class PowerShellScriptRunner
{
    /// <summary>执行脚本（默认 <c>-NoProfile -ExecutionPolicy Bypass -File &lt;script&gt; [args]</c>）。</summary>
    public static async Task<PowerShellScriptResult> RunFileAsync(
        string scriptPath,
        IEnumerable<string>? arguments = null,
        PowerShellScriptOptions? options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            throw new ArgumentException("脚本路径不得为空。", nameof(scriptPath));

        var opts = options ?? new PowerShellScriptOptions();
        if (!File.Exists(scriptPath))
        {
            throw new PowerShellHostScriptException(
                PowerShellHostCodes.ScriptMissing,
                $"PowerShell 脚本不存在：{scriptPath}",
                scriptPath);
        }

        var executable = !string.IsNullOrWhiteSpace(opts.ResolvedExecutable)
            ? opts.ResolvedExecutable!
            : PowerShellHostLocator.Locate(null, opts.AllowWindowsPowerShell51);
        var workingDirectory = ResolveWorkingDirectory(opts.WorkingDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (opts.NoProfile) startInfo.ArgumentList.Add("-NoProfile");
        if (opts.NonInteractive) startInfo.ArgumentList.Add("-NonInteractive");
        if (opts.BypassExecutionPolicy)
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                if (argument is null) continue;
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (opts.Environment is not null)
        {
            foreach (var pair in opts.Environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new PowerShellHostScriptException(
                    PowerShellHostCodes.ScriptStartFailed,
                    $"PowerShell 进程启动失败：{executable}",
                    executable);
            }
        }
        catch (PowerShellHostScriptException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PowerShellHostScriptException(
                PowerShellHostCodes.ScriptStartFailed,
                $"PowerShell 进程启动失败（{ex.GetType().Name}）：{executable} {ex.Message}",
                executable,
                inner: ex);
        }

        // 先起排空（并发读双流），再等待退出：大输出不会与子进程互等。
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var timedOut = false;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (opts.Timeout is { } limit && limit > TimeSpan.Zero)
            timeoutCts.CancelAfter(limit);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
        }

        elapsed.Stop();
        var stdout = await SafeAwait(stdoutTask).ConfigureAwait(false);
        var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);
        var exitCode = timedOut ? -1 : process.ExitCode;

        return new PowerShellScriptResult(exitCode, stdout, stderr, timedOut, elapsed.Elapsed);
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
}
