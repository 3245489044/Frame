using System.Text.Json;
using CallCenter.Cost.Instrumentation;

namespace CallCenter.Cost.Metering;

/// <summary>
/// PS / 子进程成本计量（Job Object 主实现，四级回退）。
/// 墙钟必测；CPU/内存/IO/进程数不可得 ⇒ null（Unknown），不填 0。
/// </summary>
public sealed class ProcessCostMeter : IProcessCostMeter
{
    public async ValueTask<CostSpan> RunAsync(
        InstanceRef instance,
        string filePath,
        IReadOnlyList<string> args,
        ProcessMeterOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        options ??= new ProcessMeterOptions();

        var startedAt = DateTimeOffset.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var footprintBefore = options.DiskMode is DiskMeterMode.Footprint or DiskMeterMode.Both
            ? DiskFootprintSampler.Sample(options.FootprintRoots, options.MaxFootprintDepth, options.MaxFootprintFiles)
            : null;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = options.CaptureOutput,
            RedirectStandardError = options.CaptureOutput,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
        };
        if (args is not null)
        {
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }
        if (options.Environment is not null)
        {
            foreach (var pair in options.Environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var job = JobObjectNative.TryCreateKillOnClose();
        using var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();

        var assigned = false;
        if (job is not null)
        {
            try { assigned = JobObjectNative.Assign(job, process.Handle); }
            catch { assigned = false; }
        }

        var stdoutTask = options.CaptureOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        var stderrTask = options.CaptureOutput ? process.StandardError.ReadToEndAsync() : Task.FromResult(string.Empty);

        var timedOut = false;
        var cancelled = false;
        using var timeoutCts = options.Timeout is { } timeout ? new CancellationTokenSource(timeout) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts?.IsCancellationRequested == true && !ct.IsCancellationRequested;
            cancelled = ct.IsCancellationRequested;
            if (job is not null)
            {
                JobObjectNative.Terminate(job);
            }

            try { process.Kill(entireProcessTree: options.KillTreeOnTimeout); } catch { /* 已退出 */ }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* 已退出 */ }
        }

        sw.Stop();
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        var accounting = job is null ? null : JobObjectNative.Query(job);
        var exitCode = TryGetExitCode(process);

        var footprintAfter = options.DiskMode is DiskMeterMode.Footprint or DiskMeterMode.Both
            ? DiskFootprintSampler.Sample(options.FootprintRoots, options.MaxFootprintDepth, options.MaxFootprintFiles)
            : null;
        var footprintDelta = DiskFootprintSampler.Delta(footprintBefore, footprintAfter);

        var usage = BuildUsage(
            sw.ElapsedMilliseconds,
            accounting,
            process,
            options,
            assigned,
            exitCode,
            stdout,
            stderr,
            footprintDelta,
            timedOut,
            cancelled);

        usage = ApplySelfReported(usage, stdout, options.SelfReportedMarker);

        return new CostSpan(
            SpanId: Guid.NewGuid().ToString("N"),
            ParentSpanId: options.ParentSpanId,
            TraceId: options.TraceId,
            Instance: instance,
            Kind: CostKind.Compute,
            StartedAt: startedAt,
            DurationMs: sw.ElapsedMilliseconds,
            Llm: null,
            Compute: usage,
            Tool: null,
            Price: null,
            Cost: null,
            Attributes: MergeAttributes(options.Attributes, options.Container, options.RoundId, options.TaskKey));
    }

    private static ComputeUsage BuildUsage(
        long wallMs,
        JobAccounting? accounting,
        System.Diagnostics.Process process,
        ProcessMeterOptions options,
        bool assigned,
        int? exitCode,
        string stdout,
        string stderr,
        long? footprintDelta,
        bool timedOut,
        bool cancelled)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["timed_out"] = timedOut ? "true" : "false",
            ["cancelled"] = cancelled ? "true" : "false",
            ["job_assigned"] = assigned ? "true" : "false",
        };

        if (accounting is not null)
        {
            return new ComputeUsage(
                wallMs,
                accounting.UserMs,
                accounting.KernelMs,
                accounting.PeakWorkingSetBytes > 0 ? accounting.PeakWorkingSetBytes : null,
                accounting.PeakJobMemoryBytes > 0 ? accounting.PeakJobMemoryBytes : null,
                accounting.ReadBytes,
                accounting.WriteBytes,
                footprintDelta,
                accounting.TotalProcesses > 0 ? accounting.TotalProcesses : null,
                Encoding.UTF8.GetByteCount(stdout ?? string.Empty),
                Encoding.UTF8.GetByteCount(stderr ?? string.Empty),
                exitCode,
                ComputeBases.JobObject,
                options.MemoryBasis,
                options.DiskMode,
                attributes);
        }

        var cpu = TryRead(() => (long)process.TotalProcessorTime.TotalMilliseconds);
        var peakWorkingSet = TryRead(() => process.PeakWorkingSet64);
        var peakPaged = TryRead(() => process.PeakPagedMemorySize64);
        var hasSingleProcess = cpu is not null || peakWorkingSet is not null;

        attributes["job_object_unavailable"] = "true";
        return new ComputeUsage(
            wallMs,
            cpu,
            null,
            peakWorkingSet,
            peakPaged,
            null,
            null,
            footprintDelta,
            null,
            Encoding.UTF8.GetByteCount(stdout ?? string.Empty),
            Encoding.UTF8.GetByteCount(stderr ?? string.Empty),
            exitCode,
            hasSingleProcess ? ComputeBases.SingleProcess : ComputeBases.WallOnly,
            options.MemoryBasis,
            options.DiskMode,
            attributes);
    }

    private static ComputeUsage ApplySelfReported(
        ComputeUsage usage,
        string stdout,
        string? marker)
    {
        if (string.IsNullOrWhiteSpace(marker) || string.IsNullOrWhiteSpace(stdout))
        {
            return usage;
        }

        var line = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.StartsWith(marker, StringComparison.Ordinal));
        if (line is null)
        {
            return usage;
        }

        try
        {
            using var doc = JsonDocument.Parse(line[marker.Length..]);
            var root = doc.RootElement;
            var attributes = new Dictionary<string, string>(usage.Attributes ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["self_reported"] = "true",
            };

            return usage with
            {
                CpuUserMs = ReadLong(root, "cpu_user_ms") ?? usage.CpuUserMs,
                CpuKernelMs = ReadLong(root, "cpu_kernel_ms") ?? usage.CpuKernelMs,
                PeakWorkingSetBytes = ReadLong(root, "peak_working_set_bytes") ?? usage.PeakWorkingSetBytes,
                IoReadBytes = ReadLong(root, "io_read_bytes") ?? usage.IoReadBytes,
                IoWriteBytes = ReadLong(root, "io_write_bytes") ?? usage.IoWriteBytes,
                ProcessCount = ReadInt(root, "process_count") ?? usage.ProcessCount,
                ExitCode = ReadInt(root, "exit_code") ?? usage.ExitCode,
                Basis = ComputeBases.SelfReported,
                Attributes = attributes,
            };
        }
        catch
        {
            // 自报格式错误 ⇒ 保留已测值（不把坏数据当事实）
            return usage;
        }
    }

    private static long? ReadLong(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static T? TryRead<T>(Func<T> read) where T : struct
    {
        try { return read(); } catch { return null; }
    }

    private static int? TryGetExitCode(System.Diagnostics.Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch { return null; }
    }

    private static IReadOnlyDictionary<string, string> MergeAttributes(
        IReadOnlyDictionary<string, string>? attributes,
        string? container,
        string? roundId,
        string? taskKey)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                result[pair.Key] = pair.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(container)) result["container"] = container!;
        if (!string.IsNullOrWhiteSpace(roundId)) result["round_id"] = roundId!;
        if (!string.IsNullOrWhiteSpace(taskKey)) result["task_key"] = taskKey!;
        return result;
    }
}

/// <summary>进程内动作计量（墙钟基线）。</summary>
public sealed class StopwatchCostMeter : ICostMeter
{
    public async ValueTask<CostSpan> MeasureAsync(
        InstanceRef instance,
        Func<CancellationToken, ValueTask> body,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(body);

        var startedAt = DateTimeOffset.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await body(ct).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
        }

        return new CostSpan(
            Guid.NewGuid().ToString("N"),
            null,
            null,
            instance,
            CostKind.Compute,
            startedAt,
            sw.ElapsedMilliseconds,
            null,
            new ComputeUsage(sw.ElapsedMilliseconds, null, null, null, null, null, null, null, null,
                null, null, null, ComputeBases.WallOnly),
            null,
            null,
            null);
    }
}
