namespace Share.Messaging;

/// <summary>
/// 物理投递端口：只做 Send/Read/Query/Ack/Fail，不含业务判断。
/// <para>实现方负责「逻辑 <see cref="MessageEnvelope.MessageId"/> ↔ 物理键」映射，物理键迁移期保持不变。</para>
/// <para>未装配依赖 ⇒ 返回显式失败回执（<see cref="MessageDiagnostics.TransportUnwired"/>），禁伪造成功。</para>
/// </summary>
public interface IMessageTransport
{
    /// <summary>发送（发布/投递）。</summary>
    Task<MessageReceipt> SendAsync(MessageEnvelope message, CancellationToken ct = default);

    /// <summary>按逻辑消息 Id 读取（不存在 ⇒ null，不抛）。</summary>
    Task<MessageEnvelope?> ReadAsync(string messageId, CancellationToken ct = default);

    /// <summary>有界查询（无检索面 ⇒ 空集，调用方以回执/诊断区分「无结果」与「不支持」）。</summary>
    Task<IReadOnlyList<MessageEnvelope>> QueryAsync(MessageQuery query, CancellationToken ct = default);

    /// <summary>确认（终态）。</summary>
    Task<MessageReceipt> AckAsync(string messageId, string? outcome = null, CancellationToken ct = default);

    /// <summary>置失败（<paramref name="deadLetter"/>=true ⇒ 死信终态）。</summary>
    Task<MessageReceipt> FailAsync(string messageId, string reason, bool deadLetter = true, CancellationToken ct = default);
}
