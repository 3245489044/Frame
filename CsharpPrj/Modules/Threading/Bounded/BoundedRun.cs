using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Threading.Timing;

namespace Modules.Threading;

/// <summary>有界执行结局三态（外层取消与预算超时互斥且外层优先）。</summary>
public enum BoundedRunOutcomeKind
{
    /// <summary>body 在超时窗口内完成（胜出）——完成即真实，取消发生在完成之后不影响已返回结果。</summary>
    Completed,

    /// <summary>定时器胜出且外层未取消：预算超时（body 已优雅排空，上限 drainTimeout）。</summary>
    TimedOut,

    /// <summary>外层取消优先于预算超时（调用方按取消上抛，不误报超时）。</summary>
    OuterCanceled,
}

/// <summary>有界执行结局：<see cref="Kind"/> 三态 + <see cref="Value"/>（仅 Completed 有效）。</summary>
public readonly record struct BoundedRunOutcome<T>(BoundedRunOutcomeKind Kind, T? Value);

/// <summary>
/// 有界执行尾处理单源：在共享 <see cref="BoundedRace"/>（linked-CTS + CancelAfter + WhenAny 真竞速核心）之上，
/// 收敛「外层取消优先 + 优雅排空 + 三态分类」，消除各调用点各自重写的三分支尾处理（语义易漂移）。
/// 各调用点仅映射自身「超时动作」（抛强类型异常 / 置 Timeout 状态）。
/// <para>
/// 与同级 <see cref="BoundedExecution"/>（层级 CTS 边界载体：树形派生 + 自身预算抛 <see cref="BoundedTimeoutException"/>）
/// 分工不同：本类只做**单次有界调用的三态尾处理**，不做 CTS 树与层级预算继承。
/// </para>
/// 竞速原语不在此重复实现（保持 Frame 层单源）。
/// </summary>
public static class BoundedRun
{
    /// <summary>默认优雅排空上限（与既有各调用点一致）。</summary>
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>有界执行：<paramref name="timeout"/>≤0 走直通（不竞速，零回归）。</summary>
    public static async Task<BoundedRunOutcome<T>> RunAsync<T>(
        Func<CancellationToken, Task<T>> body, TimeSpan timeout, CancellationToken outer,
        TimeSpan? drainTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (timeout <= TimeSpan.Zero)
            return new BoundedRunOutcome<T>(BoundedRunOutcomeKind.Completed, await body(outer).ConfigureAwait(false));

        using var race = await BoundedRace.StartAsync(body, timeout, outer).ConfigureAwait(false);

        // body 胜出 = 完成即真实：定时器随后触发也不丢已完成轮（胜出由 body 终态判定，见 BoundedRace.Completed）。
        // 两类例外：① body 因外层取消而终止（OCE）→ 外层取消优先，归 OuterCanceled；
        //           ② body 因预算取消而终止 → 非「完成」，落下方超时路径（分类不外泄异常）。
        if (race.Completed)
        {
            try
            {
                return new BoundedRunOutcome<T>(BoundedRunOutcomeKind.Completed, await race.Body.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (outer.IsCancellationRequested)
            {
                return new BoundedRunOutcome<T>(BoundedRunOutcomeKind.OuterCanceled, default);
            }
            catch (OperationCanceledException)
            {
                // body 被预算取消（非外层取消）→ 落超时路径：三态分类必须**完备**，OCE 不得外泄为未知失败。
            }
        }

        // 定时器胜出：外层取消优先（不误报预算超时）；否则预算超时 → 优雅排空 body（上限 drainTimeout）后归 TimedOut。
        if (race.OuterCanceled)
            return new BoundedRunOutcome<T>(BoundedRunOutcomeKind.OuterCanceled, default);

        await DrainAsync(race.Body, drainTimeout ?? DefaultDrainTimeout, outer).ConfigureAwait(false);
        return new BoundedRunOutcome<T>(BoundedRunOutcomeKind.TimedOut, default);
    }

    /// <summary>void body 重载（结果占位 true）。</summary>
    public static Task<BoundedRunOutcome<bool>> RunAsync(
        Func<CancellationToken, Task> body, TimeSpan timeout, CancellationToken outer,
        TimeSpan? drainTimeout = null)
        => RunAsync<bool>(
            async ct => { await body(ct).ConfigureAwait(false); return true; },
            timeout, outer, drainTimeout);

    /// <summary>优雅排空：等 body 至多 <paramref name="drainTimeout"/>（或外层取消），不抛（对齐原各调用点 <c>catch { }</c>）。</summary>
    private static async Task DrainAsync<T>(Task<T> body, TimeSpan drainTimeout, CancellationToken outer)
    {
        try
        {
            await Task.WhenAny(body, Task.Delay(drainTimeout, outer)).ConfigureAwait(false);
        }
        catch
        {
            // 排空尽力而为：body 最终异常不影响超时尾处理（原实现同语义）。
        }
    }
}
