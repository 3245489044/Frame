using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Share.TaskStatusPool.Routing;

namespace Operation.HierarchyMethod.Reference;

/// <summary>
/// 执行标识符后台反应式驱动（设计 §5）：SetFlag / SetFlagScope 写图元数据后经 <see cref="Signal"/> 发信号，
/// 自动执行「①解析 → ②路由 → ③运行 → ④回切 Std」闭环，无需调用方手写各步。
/// <para>本驱动只编排、不写边、不新增执行引擎（SingleImplementation / TH-12）。「运行」经注入 runPort 落到
/// 既有任务池/执行器解析路径（组合根把 Llm/Rule 映射到 SchedulingMode 或直接喂 ExecutionFlagAutonomy）；未注入 runPort → 直接完成（仅状态编排）。</para>
/// <para>LLM-free：本组件不引用任何 LLM / Agent 执行类型，只承载反应式状态编排。</para>
/// </summary>
public sealed class FlagConductor : IDisposable
{
    private readonly ExecutionFlagService _svc;
    private readonly Channel<EntityRef> _signals = Channel.CreateUnbounded<EntityRef>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private Task? _drain;
    private Func<EntityRef, ExecutionFlag, CancellationToken, Task>? _runPort;

    /// <param name="svc">统一执行标识符服务（编排目标）。</param>
    public FlagConductor(ExecutionFlagService svc)
        => _svc = svc ?? throw new ArgumentNullException(nameof(svc));

    /// <summary>注入运行端口（把生效标志落到既有任务池执行；可空 = 仅状态编排，零执行）。</summary>
    public void SetRunPort(Func<EntityRef, ExecutionFlag, CancellationToken, Task>? port) => _runPort = port;

    /// <summary>反应式触发点：写入图元数据后调用，入队并启动后台 drain（fire-and-forget）。</summary>
    public void Signal(EntityRef entity)
    {
        _signals.Writer.TryWrite(entity.Normalize());
        lock (this)
        {
            if (_drain is null || _drain.IsCompleted)
                _drain = DrainAsync(_cts.Token);
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        while (await _signals.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            while (_signals.Reader.TryRead(out var e))
                await ProcessAsync(e, ct).ConfigureAwait(false);
    }

    /// <summary>反应式闭环（可 await，测试用）：①解析 → ②路由 → ③运行 → ④回切 Std。</summary>
    public async Task ProcessAsync(EntityRef entity, CancellationToken ct)
    {
        var norm = entity.Normalize();
        // ① 解析生效标志（沿引用边向上找最近非 Auto/Std 父层）
        var effective = await _svc.ResolveFlagAsync(norm, ct).ConfigureAwait(false);
        // ② 路由（复用 TaskRouter canonical：ExecutionFlagAutonomy → ExecutionDisposition）
        var disposition = TaskRouter.Route(ExecutionFlagRouteItem.Instance, ExecutionFlagAutonomy.From(effective), ruleBackend: null).Disposition;
        await _svc.UpdateLifecycleAsync(norm, FlagLifecycleState.Routed, ct).ConfigureAwait(false);
        await _svc.UpdateLifecycleAsync(norm, FlagLifecycleState.Running, ct).ConfigureAwait(false);
        // ③ 运行（经注入 runPort 落到已适配框架；未装配 → 直接完成，仅状态编排）
        try
        {
            if (_runPort is not null)
                await _runPort(norm, effective, ct).ConfigureAwait(false);
            await _svc.UpdateLifecycleAsync(norm, FlagLifecycleState.Completed, ct).ConfigureAwait(false);
            await _svc.WriteResultAsync(norm, effective, "completed", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _svc.UpdateLifecycleAsync(norm, FlagLifecycleState.Cancelled, ct).ConfigureAwait(false);
        }
        catch
        {
            await _svc.UpdateLifecycleAsync(norm, FlagLifecycleState.Faulted, ct).ConfigureAwait(false);
            await _svc.WriteResultAsync(norm, effective, "faulted", ct).ConfigureAwait(false);
        }
        // ④ 回切标准标识符（exec.flag=Std、lifecycle=idle，回稳态等待下一次 SetFlag）
        await _svc.ResetAsync(norm, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

/// <summary>TaskRouter 路由占位 item（flag 级路由无具体任务；仅承载自主性判定，复用 TaskRouter canonical / TH-12）。</summary>
public sealed record ExecutionFlagRouteItem
{
    /// <summary>单例占位。</summary>
    public static readonly ExecutionFlagRouteItem Instance = new();
}
