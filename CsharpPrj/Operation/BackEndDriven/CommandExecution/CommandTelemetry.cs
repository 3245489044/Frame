using System.Diagnostics;
using System.Collections.Generic;
using Modules.Threading;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// 命令运行期遥测（S1·实时错误/慢-卡死报告）。
/// 解决“错误只在整条脚本结束才汇总、卡死的单条命令永不汇总”的实时盲区：
/// 在命令边界 begin/失败/超慢/卡死时**即时**出声，而不是结束才写 ExecutionResultMatrix。
/// 报告型（不打断执行、不触碰状态），健康/快速命令零开销（仅失败或超阈才 emit）。
/// </summary>
public interface ICommandTelemetry
{
    /// <summary>慢/卡死报告阈值等配置。</summary>
    CommandTelemetryOptions Options { get; }

    /// <summary>命令开始执行（低噪声进度；是否输出由 <see cref="CommandTelemetryOptions.BeginTrace"/> 控制）。</summary>
    void OnCommandBegin(int index, string rawCommand, string? action, string? target);

    /// <summary>命令失败（即时错误报告；异常可带完整堆栈供“详细错误”）。</summary>
    void OnCommandFailure(int index, string rawCommand, string? action, string? target, string error, long elapsedMs, Exception? exception = null);

    /// <summary>命令已运行超过慢阈值 / 疑似卡死（报告型；isHang=false 慢提示，true 升级为卡死）。</summary>
    void OnCommandSlowOrHang(int index, string rawCommand, string? action, string? target, long elapsedMs, bool isHang);
}

/// <summary>命令遥测阈值/开关（单一可配点；Slow 必须 &lt; Hang，两者 &gt;0 才启用对应报告）。</summary>
public sealed class CommandTelemetryOptions
{
    /// <summary>慢报告阈值：单条命令持续执行超过此仍未返回即报“慢/疑似卡死”提示。默认 15s。</summary>
    public TimeSpan SlowReport { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>卡死报告阈值：超过即升级为“卡死”报告（含 dump 建议）。默认对齐 <see cref="TimingDefaults.HangStall"/>（30s）。</summary>
    public TimeSpan HangReport { get; set; } = TimingDefaults.HangStall;

    /// <summary>是否打印每条命令 begin 进度行（默认关——健康路径保持静默，防刷屏）。</summary>
    public bool BeginTrace { get; set; }

    /// <summary>命令执行轨迹环容量（卡死时给出前序命令/脚本进度上下文）。默认 40。</summary>
    public int CommandTraceCapacity { get; set; } = 40;
}

/// <summary>
/// 默认控制台遥测实现：实时行写 <see cref="Console.Out"/>（FileDriven GUI/管道下被重定向到命名管道→终端），
/// 并以结构化消息写 <see cref="BackEndDriven.Diagnostics.BackEndLog"/> 持久化。
/// </summary>
public sealed class ConsoleCommandTelemetry : ICommandTelemetry
{
    private readonly CommandTelemetryOptions _options;
    private readonly RingBuffer<string> _commandTrace;
    private readonly HangEvidenceAssembler _assembler;

    /// <param name="options">慢/卡死阈值与追踪开关（缺省默认）。</param>
    /// <param name="evidenceSources">可选的资源侧取证源（db/ui/outbox 等），卡死取证瞬间经 <see cref="HangEvidenceAssembler"/> 统一装配进证据包；可空。</param>
    public ConsoleCommandTelemetry(CommandTelemetryOptions? options = null, IEnumerable<IHangEvidenceSource>? evidenceSources = null)
    {
        options ??= new CommandTelemetryOptions();
        _options = options;
        _commandTrace = new RingBuffer<string>(options.CommandTraceCapacity);
        IReadOnlyList<IHangEvidenceSource>? sources = null;
        if (evidenceSources is not null)
            sources = new List<IHangEvidenceSource>(evidenceSources);
        _assembler = new HangEvidenceAssembler(_commandTrace, sources);
    }

    public CommandTelemetryOptions Options => _options;

    public void OnCommandBegin(int index, string rawCommand, string? action, string? target)
    {
        // R1（条件写环）：健康/快命令不写执行轨迹环，保持零开销——仅显式追踪(BeginTrace)才入环。
        // 失败/卡死路径（OnCommandFailure / 取证块）仍无条件写环，保证卡死取证能读到前序命令上下文。
        if (!_options.BeginTrace) return;
        _commandTrace.Add($"[#{index}] {rawCommand}");
        Console.WriteLine($"[cmd #{index}] {rawCommand}");
    }

    public void OnCommandFailure(int index, string rawCommand, string? action, string? target, string error, long elapsedMs, Exception? exception = null)
    {
        _commandTrace.Add($"[#{index}] 失败 {rawCommand}");
        Console.WriteLine($"[运行错误] 命令 #{index} 执行失败({elapsedMs}ms): {rawCommand}");
        Console.WriteLine($"           原因: {error}");
        if (exception != null)
            Console.WriteLine($"           异常: {exception}");
        Diagnostics.BackEndLog.Log("CommandCoordinator_realtime_failure",
            new { index, rawCommand, action, target, error, elapsedMs });
    }

    public void OnCommandSlowOrHang(int index, string rawCommand, string? action, string? target, long elapsedMs, bool isHang)
    {
        var seconds = elapsedMs / 1000.0;
        if (isHang)
        {
            Console.WriteLine($"[运行期诊断·卡死] 命令 #{index} 已停滞 {seconds:0.#}s 仍未返回: {rawCommand}");
            // 判卡取证单一归口：经 HangEvidenceAssembler 装配（执行轨迹环 + 注入资源探针）→ 再渲染进程证据块。
            var evidence = _assembler.Assemble(
                probe: null,
                stallMilliseconds: elapsedMs,
                verdict: HangVerdict.ConfirmedHang,
                runKey: action,
                step: rawCommand);
            PrintEvidenceBlock(index, target, elapsedMs, evidence);
            Diagnostics.BackEndLog.Log("CommandCoordinator_realtime_hang",
                new { index, rawCommand, action, target, elapsedMs });
        }
        else
        {
            Console.WriteLine($"[运行期诊断] 命令 #{index} 已执行 {seconds:0.#}s 仍未返回(疑似慢/卡死): {rawCommand}");
            Console.WriteLine($"            action={action} target={target}");
            Diagnostics.BackEndLog.Log("CommandCoordinator_realtime_slow",
                new { index, rawCommand, action, target, elapsedMs });
        }
    }

    /// <summary>卡死时打印进程证据块：被卡命令轨迹 + 资源探针快照 + 进程/线程/线程池/内存快照（不落 dump 文件）。</summary>
    private void PrintEvidenceBlock(int index, string? target, long elapsedMs, HangEvidence evidence)
    {
        var pid = Environment.ProcessId;
        try
        {
            Console.WriteLine("  ── 进程证据 ─────────────────────────────");

            // 命令执行轨迹尾（前序命令 → 被卡命令），反映脚本推进到哪（由装配器统一快照）
            var trace = evidence.RecentLogs;
            Console.WriteLine($"  命令轨迹(最近 {trace.Count} 条, 末位≈被卡命令):");
            foreach (var line in trace)
                Console.WriteLine($"    {line}");

            // 注入的资源侧探针快照（db/ui/outbox 等，取证瞬间抓取；可空）
            if (evidence.Context.Count > 0)
            {
                Console.WriteLine("  资源探针快照:");
                foreach (var kv in evidence.Context)
                    Console.WriteLine($"    {kv.Key}: {kv.Value}");
            }

            // 进程/线程/线程池/内存快照（廉价，不需跨线程取托管栈）
            using var proc = Process.GetCurrentProcess();
            ThreadPool.GetAvailableThreads(out int availW, out int availIo);
            ThreadPool.GetMaxThreads(out int maxW, out int maxIo);
            Console.WriteLine($"  process: pid={pid} threads={proc.Threads.Count} workingSetMB={proc.WorkingSet64 / (1024 * 1024)}");
            Console.WriteLine($"  threadpool: workerBusy={maxW - availW}/{maxW} iocpBusy={maxIo - availIo}/{maxIo} pending={ThreadPool.PendingWorkItemCount} completed={ThreadPool.CompletedWorkItemCount}");
            Console.WriteLine($"  gc: heapMB={GC.GetTotalMemory(false) / (1024 * 1024)}  command=#{index} target={target} elapsedMs={elapsedMs}");

            // 锁归属诊断仅停留内存/控制台，不落任何 dump 文件到磁盘（遵守"debug 不往硬盘写文件"约定）。
            // 写锁长持的 owner 定位走进程内自报告（Agent OutboxWriter_flush_slow / NodeExecutionService 读源打点），无需离线 dump。
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (进程证据采集失败: {ex.Message})");
        }
        Console.WriteLine("  ───────────────────────────────────────────");
    }
}
