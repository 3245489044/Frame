using System;
using System.Threading;
using System.Threading.Tasks;
using Share.Signal;

namespace Share.Signal;

/// <summary>
/// 信号分发端口（宿主注入：唯一触发出口 = 会话发送；与 IInjectionPort / ITransferFlow 同型）。
/// <para>SCF：投递返回回执（只证明受理/在途，不证明目标完成）。旧 <see cref="Send"/> 保留一版作为适配器。</para>
/// <para>未装配 ⇒ 只暂存不分发（零回归，不抛）。</para>
/// </summary>
public sealed class SignalDispatchPort
{
    /// <summary>旧出口（target → 会话发送）；保留兼容。</summary>
    public Func<string, string, CancellationToken, Task>? Send { get; init; }

    /// <summary>新出口（宿主可注入带回执实现；null ⇒ 回退旧 Send）。</summary>
    public Func<SignalEnvelopeContract, CancellationToken, Task>? Dispatch { get; init; }

    /// <summary>依赖满足判定（如"全部成员已完成"）；null = 视为不满足。</summary>
    public Func<string, CancellationToken, Task<bool>>? DependencySatisfied { get; init; }

    /// <summary>分发一条信号并返回回执。</summary>
    public async Task<SignalReceipt> DispatchAsync(SignalEnvelopeContract envelope, CancellationToken ct = default)
    {
        if (Dispatch is not null)
        {
            await Dispatch(envelope, ct).ConfigureAwait(false);
            return new SignalReceipt(envelope.Id, LeaseIdOf(envelope), SignalState.Dispatched, "dispatched");
        }

        if (Send is null)
            return new SignalReceipt(envelope.Id, string.Empty, SignalState.Accepted, "dispatch_port_unbound");

        var payload = string.IsNullOrWhiteSpace(envelope.Payload) ? envelope.Target : envelope.Payload!;
        await Send(envelope.Target, payload, ct).ConfigureAwait(false);
        return new SignalReceipt(envelope.Id, LeaseIdOf(envelope), SignalState.Dispatched, "dispatched");
    }

    private static string LeaseIdOf(SignalEnvelopeContract envelope)
        => $"lease-{envelope.Key.Value.GetHashCode():x}-{envelope.Attempt}";
}
