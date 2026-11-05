namespace Share.Messaging;

/// <summary>
/// 统一消息信封（领域无关）：所有适配器进出都先映射到本类型。
/// <para><see cref="State"/>/<see cref="Revision"/> 承载生命周期与 CAS 版本（适配器负责写入与比较）。</para>
/// <para><see cref="DeadlineUtc"/> 为 ISO8601 字符串：内核不取时钟，比较时由调用方传入 nowUtc。</para>
/// </summary>
public sealed record MessageEnvelope(
    string MessageId,
    int Version,
    MessageKind Kind,
    string Protocol,
    MessageAddress From,
    MessageAddress? To,
    string Scope,
    string? CorrelationId = null,
    string? CausationId = null,
    string? ParentId = null,
    string? TraceId = null,
    int Hop = 0,
    string? ContractId = null,
    string? ContractVersion = null,
    string? Payload = null,
    string? PayloadRef = null,
    string? PayloadDigest = null,
    int? TtlSeconds = null,
    string? DeadlineUtc = null,
    string? IdempotencyKey = null,
    string? ReplyTo = null,
    string? Error = null,
    MessageState State = MessageState.Created,
    int Revision = 0,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SubAgentTag = null)
{
    /// <summary>恒定摘要（未显式给出时由内核计算，保证「恒有摘要」）。</summary>
    public string EffectiveDigest
        => string.IsNullOrWhiteSpace(PayloadDigest)
            ? MessageCodec.Digest(Payload ?? PayloadRef)
            : PayloadDigest!;

    /// <summary>是否终态（终态不可复活）。</summary>
    public bool IsTerminal => MessageStateMachine.IsTerminal(State);

    /// <summary>补齐摘要（缺省摘要写入信封，保持不可变语义）。</summary>
    public MessageEnvelope WithDigest()
        => string.IsNullOrWhiteSpace(PayloadDigest) ? this with { PayloadDigest = EffectiveDigest } : this;

    /// <summary>状态迁移（同状态重放 = 幂等无操作、Revision 不变；非法迁移抛 <see cref="InvalidOperationException"/>；合法则 Revision+1）。</summary>
    public MessageEnvelope Transition(MessageState next)
    {
        if (State == next) return this;
        if (!MessageStateMachine.TryTransition(State, next, out var reason))
            throw new InvalidOperationException($"{MessageId}: {reason}");
        return this with { State = next, Revision = Revision + 1 };
    }
}
