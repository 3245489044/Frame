using System.Threading;
using System.Threading.Tasks;

namespace Share.Signal;

/// <summary>
/// 信号分发端口（SCF 唯一出口）—— 返回回执，不把“调用没报错”当成功。
/// <para>宿主注入实现（旧 <c>SignalDispatchPort.Send</c> 作为适配器保留一版）。</para>
/// </summary>
public interface ISignalDispatchPort
{
    /// <summary>分发一条信号；<c>Receipt.State</c> 只表示受理/在途，不表示终态。</summary>
    Task<SignalReceipt> DispatchAsync(SignalEnvelopeContract envelope, CancellationToken ct = default);
}

/// <summary>
/// 信号回执端口（SCF Ack 段）—— 目标执行体回执入口。
/// <para>允许只回 <c>(SignalKey, Attempt)</c>，服务端反查 <c>SignalId</c>（设计单 R2 裁决）。</para>
/// </summary>
public interface ISignalAckPort
{
    /// <summary>回执一条信号（幂等：重复终态不重复结算）。</summary>
    Task AckAsync(SignalKey key, int attempt, SignalOutcome outcome, string? reason, CancellationToken ct = default);
}

/// <summary>
/// 契约层信号信封（与 <c>Agent.Session.Signal.SignalEnvelope</c> 同构的可移植视图）——
/// 让端口不依赖 Session 程序集；Session 侧提供转换。
/// </summary>
public readonly record struct SignalEnvelopeContract(
    SignalId Id,
    SignalKey Key,
    int Attempt,
    string Scope,
    string Target,
    string Kind,
    string? Payload,
    long Timestamp,
    int Priority);
