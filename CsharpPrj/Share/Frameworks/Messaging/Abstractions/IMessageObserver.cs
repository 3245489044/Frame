namespace Share.Messaging;

/// <summary>观测端口（只读计数，不参与决策）。</summary>
public interface IMessageObserver
{
    /// <summary>已发送（发布成功）。</summary>
    void OnSent(MessageEnvelope message, DeliveryClass delivery);

    /// <summary>状态迁移（用于生命周期计数：Acked/Failed/Expired/DeadLetter）。</summary>
    void OnTransition(string messageId, MessageState from, MessageState to, string? code = null);
}
