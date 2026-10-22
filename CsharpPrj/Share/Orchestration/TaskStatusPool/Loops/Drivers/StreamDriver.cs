using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.Loops.Drivers;

/// <summary>
/// 事件/定时流壳：每次触发执行一次（经 <see cref="LoopKernel"/>），只由宿主生命周期令牌终止。
/// 「长驻事件循环」不是「重试循环」：单次失败不置回、不重拍、不退避重发（失败只经 <paramref name="onTick"/> 可见化）。
/// </summary>
public static class StreamDriver
{
    public static async Task RunAsync(
        Func<CancellationToken, Task<bool>> waitTick,
        Func<CancellationToken, Task<StepOutcome>> onTick,
        CancellationToken lifetime)
    {
        PortGuard.Required(waitTick, nameof(waitTick));
        PortGuard.Required(onTick, nameof(onTick));

        // 唯一许可的事件等待循环：退出条件只有宿主生命周期（AX-CORE.EssentiallyUndecidable）。
        while (!lifetime.IsCancellationRequested)
        {
            if (!await waitTick(lifetime).ConfigureAwait(false)) break;
            await LoopKernel.ExecuteOnceAsync(0, async (_, token) => await onTick(token).ConfigureAwait(false), lifetime)
                .ConfigureAwait(false);
        }
    }
}
