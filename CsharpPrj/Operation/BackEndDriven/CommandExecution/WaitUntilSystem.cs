#nullable enable

using System.Linq;
using ECS.Components;
using ECS.Core;
using Modules.Threading;
using BackEndDriven.Diagnostics;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using Share.EventBindingOrchestrator.Invocation;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// wait-until 命令（B6·有界轮询，替代固定 sleep）：按 interval 周期调用探测函数，
/// 直到其返回文本包含 expectedContains 或到达 timeout；命中后把最后一次文本回写 output 控件。
/// <para>
/// 语义（对齐设计单 §B6）：<c>wait-until &lt;probeFn&gt; expectedContains=&lt;text&gt; timeout=&lt;ms&gt; interval=&lt;ms&gt; [output=&lt;control&gt;]</c>；
/// 失败必须落在强判据（超时 + 最后一次文本可见），而非「没跑完」。固定 sleep 由
/// <c>Check-ScriptNoFixedSleep.ps1</c> 拦截。
/// </para>
/// <para>探测函数 = 只读生产者（如 conversation/history）；本命令只调用、不写业务状态。</para>
/// </summary>
public class WaitUntilSystem : CommandSystemBase, IAsyncCommandSystem
{
    /// <summary>默认总预算（真实 LLM/daemon 链路 ≥ 固定 sleep 90000 的历史实测上界）。</summary>
    public const int DefaultTimeoutMs = 300_000;

    /// <summary>默认轮询间隔（只读探测，避免高频打库）。</summary>
    public const int DefaultIntervalMs = 2_000;

    private readonly IEcsStateAccessor? _stateAccessor;

    public WaitUntilSystem(EntityManager entityManager)
        : base(entityManager, "wait-until", "waituntil")
    {
        _stateAccessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
    }

    /// <summary>同步路径（legacy 调用方）委托异步实现；主脚本路径走非阻塞异步。</summary>
    public override void Execute(Entity entity, CommandMetaComponent meta)
        => ExecuteAsync(entity, meta).GetAwaiter().GetResult();

    public async Task ExecuteAsync(Entity entity, CommandMetaComponent meta)
    {
        var probe = meta.Target;
        if (string.IsNullOrWhiteSpace(probe))
        {
            SetFailure(entity, "wait-until: 缺少探测函数名");
            return;
        }

        if (!TryParse(meta, out var parameters, out var expectedContains, out var timeoutMs, out var intervalMs, out var outputTarget, out var failTokens))
        {
            SetFailure(entity, "wait-until: 缺少 expectedContains（空期望恒真，拒绝静默通过）");
            return;
        }

        var scope = TerminationScope.Current;
        var deadline = timeoutMs > 0 ? Environment.TickCount64 + timeoutMs : long.MaxValue;
        var token = scope?.Token ?? CancellationToken.None;
        string? lastText = null;
        var matched = false;
        var failedByTerminal = false;
        var canceled = false;
        string? frameworkFailure = null;

        while (true)
        {
            // 强制退出/外部取消：立即停止轮询（不等待探测返回）。
            if (token.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var remainingMs = deadline == long.MaxValue
                ? (long?)null
                : Math.Max(0, deadline - Environment.TickCount64);
            if (remainingMs is 0) break;

            try
            {
                // 探测本身必须有界：原语不接 ct，故以剩余 deadline 为窗口竞速；到点即放弃（不拖死命令）。
                var window = remainingMs is { } remain
                    ? TimeSpan.FromMilliseconds(remain)
                    : System.Threading.Timeout.InfiniteTimeSpan;
                // 预算穿透：探测调用的函数预算不得短于等待窗口（否则「外层 330s / 内层 30s」错配 →
                // 每 30s 空撞一次框架超时）。窗口无界（未声明 timeout）时沿用框架默认。
                var probeBudget = remainingMs is { } budget
                    ? (int)Math.Min(budget, int.MaxValue)
                    : (int?)null;
                var run = await BoundedRun.RunAsync(
                    _ => EventPlatformExecutor.InvokeAsync(probe, parameters, probeBudget),
                    window,
                    token).ConfigureAwait(false);

                if (run.Kind == BoundedRunOutcomeKind.TimedOut)
                {
                    lastText = $"探测 '{probe}' 超出等待窗口仍未返回";
                    break;
                }
                if (run.Kind == BoundedRunOutcomeKind.OuterCanceled)
                {
                    canceled = true;
                    break;
                }

                var invocation = run.Value!;
                if (!invocation.PlatformAvailable || invocation.Result is null)
                {
                    lastText = invocation.Error;
                }
                else if (!invocation.Result.IsSuccess)
                {
                    lastText = invocation.Result.ErrorMessage;
                    // 框架级终态（超时/空闲挂起/信号丢失/框架异常/对账失败）重试无意义 ⇒ 立即失败，
                    // 不再「每 30s 撞一次、空转整段 timeout」。
                    if (IsTerminalFrameworkFailure(invocation.Result.Outcome))
                    {
                        frameworkFailure = lastText;
                        break;
                    }
                }
                else
                {
                    lastText = invocation.Result.Output?.ToString() ?? string.Empty;
                    matched = lastText.IndexOf(expectedContains, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                lastText = ex.Message;
            }

            // 终态短路：任务已进入失败终态（Faulted/Cancelled/Failed/Error）⇒ 立即失败，不白等到 timeout。
            if (!matched && MatchesFailure(lastText, failTokens))
            {
                failedByTerminal = true;
                break;
            }

            if (matched || Environment.TickCount64 >= deadline) break;

            var delay = intervalMs;
            if (deadline != long.MaxValue)
            {
                var left = deadline - Environment.TickCount64;
                delay = (int)Math.Max(0, Math.Min(delay, left));
            }
            if (delay <= 0) break;

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                break;
            }
        }

        if (matched)
        {
            if (_stateAccessor != null && !string.IsNullOrWhiteSpace(outputTarget))
                _stateAccessor.SetValue(outputTarget!, lastText);

            SetSuccess(entity, lastText);
            return;
        }

        var detail = string.IsNullOrWhiteSpace(lastText) ? "(空)" : LogTextTruncation.TruncateForLog(lastText);

        // 取消 ≠ 超时：外层终止（窗体关闭/宿主终止）必须如实上报，禁冒充 IdleHung/超时（否则「被取消」永不作为成因出现）。
        if (canceled)
        {
            SetFailure(entity, $"wait-until '{probe}' 被取消（宿主终止/窗体关闭），未产出结果；最后一次: '{detail}'");
            return;
        }

        // 框架级终态：探测链路本身不可用（超时/空闲/信号丢失/框架异常/对账失败），重试无意义 ⇒ 立即失败。
        if (frameworkFailure is not null)
        {
            var reason = LogTextTruncation.TruncateForLog(frameworkFailure);
            scope?.Terminate(TerminationReason.Failed, $"wait-until '{probe}' 探测失败（框架终态）: {reason}");
            SetFailure(entity,
                $"wait-until '{probe}' 探测失败（框架终态，不再空转）: {reason}");
            return;
        }

        if (failedByTerminal)
        {
            scope?.Terminate(TerminationReason.Failed,
                $"wait-until '{probe}' 命中终态失败（failWhen）");
            SetFailure(entity,
                $"wait-until '{probe}' 终态失败（命中 failWhen）: '{detail}'");
            return;
        }

        // 超时 = 无进展终止：先请求强制退出（作用域存在时后续命令不再执行），再显式失败（不吞成普通失败）。
        scope?.Terminate(TerminationReason.IdleHung,
            $"wait-until '{probe}' 未在 {timeoutMs}ms 内命中 '{expectedContains}'");
        SetFailure(entity,
            $"wait-until '{probe}' 超时（{timeoutMs}ms）: 结果不含 '{expectedContains}' — 最后一次 '{detail}'");
    }

    /// <summary>框架级终态判据（纯函数）：该类失败重试无法恢复，等待应立即终止而非继续轮询。</summary>
    public static bool IsTerminalFrameworkFailure(InvocationOutcome outcome)
        => outcome is InvocationOutcome.FrameworkTimedOut
            or InvocationOutcome.FrameworkIdleHung
            or InvocationOutcome.FrameworkFault
            or InvocationOutcome.SignalLost
            or InvocationOutcome.ReconcileFault;

    /// <summary>终态失败判据（纯函数）：探测文本命中任一失败词即真（空词表/空文本恒假）。</summary>
    public static bool MatchesFailure(string? text, IReadOnlyList<string> failTokens)
        => !string.IsNullOrWhiteSpace(text)
           && failTokens is { Count: > 0 }
           && failTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>解析命令面：expectedContains/contains（条件）、timeout/interval/output（命令级），其余为探测函数参数。</summary>
    private static bool TryParse(
        CommandMetaComponent meta,
        out Dictionary<string, object?> parameters,
        out string expectedContains,
        out int timeoutMs,
        out int intervalMs,
        out string? outputTarget,
        out IReadOnlyList<string> failTokens)
    {
        parameters = new Dictionary<string, object?>();
        expectedContains = string.Empty;
        timeoutMs = DefaultTimeoutMs;
        intervalMs = DefaultIntervalMs;
        outputTarget = null;
        failTokens = Array.Empty<string>();

        if (meta.Parameters != null)
        {
            foreach (var kvp in meta.Parameters)
                parameters[kvp.Key] = kvp.Value;
        }

        if (parameters.TryGetValue("expectedContains", out var ec) && ec != null)
        {
            expectedContains = ec.ToString() ?? string.Empty;
            parameters.Remove("expectedContains");
        }
        else if (parameters.TryGetValue("contains", out var c) && c != null)
        {
            expectedContains = c.ToString() ?? string.Empty;
            parameters.Remove("contains");
        }

        if (string.IsNullOrWhiteSpace(expectedContains)) return false;

        // 参数解析收敛到 Share.CommandParameters（A-1 单源）。
        if (CommandParameters.TryGetInt(parameters, "timeout") is { Present: true } t)
        {
            timeoutMs = t.Value;
            parameters.Remove("timeout");
        }

        if (CommandParameters.TryGetInt(parameters, "interval") is { Present: true, Value: > 0 } iv)
        {
            intervalMs = iv.Value;
            parameters.Remove("interval");
        }

        if (CommandParameters.GetString(parameters, "output") is { } o)
        {
            outputTarget = o;
            parameters.Remove("output");
        }

        // failWhen/failContains：终态失败词表（逗号或竖线分隔）；命中即短路失败，不等到 timeout。
        var failKey = parameters.ContainsKey("failWhen") ? "failWhen"
            : parameters.ContainsKey("failContains") ? "failContains"
            : null;
        if (failKey is not null)
        {
            failTokens = CommandParameters.GetTokens(parameters, failKey);
            parameters.Remove(failKey);
        }

        return true;
    }
}
