#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading;

namespace Share.EventBindingOrchestrator.Invocation;

/// <summary>
/// 函数调用内核（Function Invocation Kernel）——任何函数调用的唯一拥有者。
/// 承载：opId 关联 / 单一权威计时（预算）/ 取消贯穿 / 失败分类裁决 / 分阶段观测 / 子进程终止。
/// 所有入口（click、exec、按钮、钩子、工作流）最终都归一到 <see cref="ExecuteAsync"/>。
/// </summary>
public static class InvocationKernel
{
    private static readonly IInvocationAdapter DefaultAdapter = new DelegateInvocationAdapter();

    /// <summary>诊断钩子（宿主注入日志器）：函数以框架态终止（超时/空闲/信号丢失）时发出真实警告。</summary>
    public static Action<string, object?>? Trace { get; set; }

    public static async Task<FunctionInvocationResult> ExecuteAsync(
        InvocationRequest request,
        IInvocationAdapter? adapter = null)
    {
        PortGuard.Required(request, nameof(request));
        if (string.IsNullOrWhiteSpace(request.FunctionName))
            return new FunctionInvocationResult(
                Guid.NewGuid(), InvocationOutcome.FunctionFault,
                false, null, "函数名称为空", 0, null, request.FunctionName);

        var opId = request.CorrelationId ?? Guid.NewGuid();
        var name = $"{request.FunctionName}:{opId}";
        var invoker = adapter ?? DefaultAdapter;
        var stages = new List<InvocationStage>();
        var stageLock = new object();
        var sw = Stopwatch.StartNew();

        var scheduler = ThreadingBridge.AefScheduler;
        if (scheduler == null)
            return await RunFallbackAsync(request, opId, invoker, stages, sw).ConfigureAwait(false);

        object? value = null;
        try
        {
            var aef = await scheduler.RunAsync(
                name,
                async (ct, op) =>
                {
                    lock (stageLock) stages.Add(new("Dispatch", sw.ElapsedMilliseconds));
                    var ctx = new KernelContext(op);
                    lock (stageLock) stages.Add(new("FunctionStart", sw.ElapsedMilliseconds));
                    value = await invoker.InvokeAsync(request, ctx).ConfigureAwait(false);
                    lock (stageLock) stages.Add(new("FunctionEnd", sw.ElapsedMilliseconds));
                    return (object?)value;
                },
                new AsyncOperationOptions
                {
                    // 单一权威计时：绝对预算来自 request；空闲窗口仅在显式请求（有真实心跳源）时启用，
                    // 否则以 TimeSpan.Zero 显式关闭——避免调度器回退到全局 30s 空闲窗口而误杀慢函数。
                    AbsoluteTimeout = request.Budget.AbsoluteMs is > 0
                        ? TimeSpan.FromMilliseconds(request.Budget.AbsoluteMs.Value)
                        : null,
                    IdleTimeout = request.Budget.IdleMs is > 0
                        ? TimeSpan.FromMilliseconds(request.Budget.IdleMs.Value)
                        : TimeSpan.Zero,
                    ReentryKey = request.ReentryKey,
                    IsolationKey = request.IsolationKey,
                    ExternalToken = request.CallerToken ?? CancellationToken.None
                }).ConfigureAwait(false);

            sw.Stop();

            // 超时/空闲挂起时 RunAsync 可能在 worker 收尾前返回 → 快照须加锁，避免读写交错。
            InvocationStage[] snapshot;
            lock (stageLock) snapshot = stages.ToArray();
            return BuildResult(request, opId, aef, value, snapshot);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new FunctionInvocationResult(opId, InvocationOutcome.SignalLost, false, null,
                $"内核执行异常: {ex.Message}", sw.ElapsedMilliseconds, stages, request.FunctionName);
        }
    }
    private static async Task<FunctionInvocationResult> RunFallbackAsync(
        InvocationRequest request,
        Guid opId,
        IInvocationAdapter invoker,
        List<InvocationStage> stages,
        Stopwatch sw)
    {
        // Fix·兜底路径补预算：AEF 未装配时也必须尊重绝对预算与调用方取消，否则挂起函数会一直烧到进程收尾。
        // 与 AEF 路径语义对齐：绝对预算到期（且外部未请求取消）→ TimedOut；外部取消 → Cancelled。
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            request.CallerToken ?? CancellationToken.None);
        if (request.Budget.AbsoluteMs is > 0)
            cts.CancelAfter(request.Budget.AbsoluteMs.Value);

        var ctx = new FallbackInvocationContext(cts.Token);
        stages.Add(new("Dispatch", sw.ElapsedMilliseconds));
        try
        {
            stages.Add(new("FunctionStart", sw.ElapsedMilliseconds));
            var output = await invoker.InvokeAsync(request, ctx).ConfigureAwait(false);
            stages.Add(new("FunctionEnd", sw.ElapsedMilliseconds));
            sw.Stop();
            return new FunctionInvocationResult(opId, InvocationOutcome.Completed,
                true, output, null, sw.ElapsedMilliseconds, stages, request.FunctionName);
        }
        catch (OperationCanceledException) when (
            request.CallerToken is { } caller && caller.IsCancellationRequested)
        {
            sw.Stop();
            return new FunctionInvocationResult(opId, InvocationOutcome.Cancelled,
                false, null, "调用被外部取消", sw.ElapsedMilliseconds, stages, request.FunctionName);
        }
        catch (OperationCanceledException) when (request.Budget.AbsoluteMs is > 0)
        {
            sw.Stop();
            return new FunctionInvocationResult(opId, InvocationOutcome.FrameworkTimedOut,
                false, null,
                $"框架绝对超时: 函数 '{request.FunctionName}' 未在预算内完成（{sw.ElapsedMilliseconds}ms）",
                sw.ElapsedMilliseconds, stages, request.FunctionName);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new FunctionInvocationResult(opId, InvocationOutcome.Cancelled,
                false, null, "调用被取消", sw.ElapsedMilliseconds, stages, request.FunctionName);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new FunctionInvocationResult(opId, InvocationOutcome.FunctionFault,
                false, null, ex.Message, sw.ElapsedMilliseconds, stages, request.FunctionName);
        }
    }

    /// <summary>兜底路径的调用上下文：携带绝对预算/取消贯穿令牌，使无 AEF 时挂起函数也能在预算点被终止。</summary>
    private sealed class FallbackInvocationContext : IInvocationContext
    {
        private readonly CancellationToken _token;

        public FallbackInvocationContext(CancellationToken token) => _token = token;

        public CancellationToken CancellationToken => _token;

        public void ReportProgress() { }

        public void RegisterProcess(Modules.Threading.IProcessHandle process) { }

        public void RegisterProcess(System.Diagnostics.Process process) { }
    }

    private static FunctionInvocationResult BuildResult(
        InvocationRequest request,
        Guid opId,
        AsyncOperationResult aef,
        object? value,
        IReadOnlyList<InvocationStage> stages)
    {
        var (outcome, error) = Classify(request, aef, stages);
        var success = outcome == InvocationOutcome.Completed;
        return new FunctionInvocationResult(opId, outcome, success,
            success ? value : null, error, aef.ElapsedMs, stages, request.FunctionName);
    }

    /// <summary>把阶段快照格式化为 "Dispatch:12ms|FunctionStart:100ms|..."，用于超时定位「函数是否真正启动」。</summary>
    private static string FormatStages(IReadOnlyList<InvocationStage> stages)
    {
        if (stages.Count == 0) return "(空)";
        return string.Join("|", stages.Select(s => $"{s.Name}:{s.ElapsedMs}ms"));
    }

    /// <summary>
    /// 失败分类裁决（唯一裁决点）：把 AEF 终结态映射为「函数错 vs 框架错」。
    /// FunctionFault=函数自身；FrameworkTimedOut/IdleHung=框架执行层；SignalLost=框架缺陷。
    /// 超时/空闲/信号丢失时附带阶段快照，帮助区分「卡在调度前」vs「卡在函数体内」。
    /// </summary>
    private static (InvocationOutcome, string?) Classify(
        InvocationRequest request,
        AsyncOperationResult aef,
        IReadOnlyList<InvocationStage>? stages = null)
    {
        switch (aef.EndPhase)
        {
            case AsyncOpPhase.Completed:
                return aef.IsSuccess
                    ? (InvocationOutcome.Completed, null)
                    : (InvocationOutcome.FunctionFault, aef.Error ?? "函数执行失败");

            case AsyncOpPhase.Failed:
                return (InvocationOutcome.FunctionFault, aef.Error ?? "函数执行失败（Failed）");

            case AsyncOpPhase.TimedOut:
                Trace?.Invoke("InvocationKernel_Terminated",
                    new { functionName = request.FunctionName, outcome = InvocationOutcome.FrameworkTimedOut.ToString(), elapsedMs = aef.ElapsedMs, stages = FormatStages(stages), error = $"未在预算内完成（{aef.ElapsedMs}ms）" });
                return (InvocationOutcome.FrameworkTimedOut,
                    $"框架绝对超时: 函数 '{request.FunctionName}' 未在预算内完成（{aef.ElapsedMs}ms）; stages=[{FormatStages(stages)}]");

            case AsyncOpPhase.IdleHung:
                Trace?.Invoke("InvocationKernel_Terminated",
                    new { functionName = request.FunctionName, outcome = InvocationOutcome.FrameworkIdleHung.ToString(), elapsedMs = aef.ElapsedMs, stages = FormatStages(stages), error = "空闲挂起" });
                return (InvocationOutcome.FrameworkIdleHung,
                    $"框架空闲挂起: 函数 '{request.FunctionName}' 在预算窗口内无任何进展（{aef.ElapsedMs}ms）; stages=[{FormatStages(stages)}]");

            case AsyncOpPhase.Cancelled:
                return (InvocationOutcome.Cancelled, aef.Error ?? "调用被取消");

            default:
                Trace?.Invoke("InvocationKernel_Terminated",
                    new { functionName = request.FunctionName, outcome = InvocationOutcome.SignalLost.ToString(), elapsedMs = aef.ElapsedMs, stages = FormatStages(stages), error = $"EndPhase={aef.EndPhase}" });
                return (InvocationOutcome.SignalLost,
                    $"完成信号未落定（EndPhase={aef.EndPhase}）——疑似框架缺陷; stages=[{FormatStages(stages)}]");
        }
    }
}

