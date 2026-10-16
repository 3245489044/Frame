using Strategy.Observability.Bus;
using Strategy.Observability.Persistence;

namespace Strategy.Observability.Publish;

/// <summary>
/// 实时出口 Sink（E5 投影）：把总线事件投递给 <see cref="LiveEventPipe"/>（零落盘）。
/// <para>
/// <b>Push 非阻塞</b>（仅入各客户端有界队列），故可安全挂在 <c>ObservabilityBus.Emit</c> 的锁内串行路径上；
/// <see cref="FlushAsync"/> 为<b>空操作</b> —— 实时出口无「落库排空」语义（不写盘）。
/// </para>
/// </summary>
public sealed class LivePipeSink : IObservabilitySink
{
    private readonly LiveEventPipe _pipe;

    public LivePipeSink(LiveEventPipe pipe)
    {
        _pipe = PortGuard.Required(pipe, nameof(pipe));
    }

    /// <summary>所服务的管道出口（诊断可见性）。</summary>
    public LiveEventPipe Pipe => _pipe;

    public void Push(PersistedEvent evt) => _pipe.Push(evt);

    /// <summary>实时出口无需排空（空操作）。</summary>
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
