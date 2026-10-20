namespace Share.Messaging;

/// <summary>观测端口（只读计数，不参与决策）。</summary>
public interface IMessageObserver
{
    /// <summary>已发送（发布成功）。</summary>
    void OnSent(MessageEnvelope message, DeliveryClass delivery);

    /// <summary>状态迁移（用于生命周期计数：Acked/Failed/Expired/DeadLetter）。</summary>
    void OnTransition(string messageId, MessageState from, MessageState to, string? code = null);

    /// <summary>投递时延采样（毫秒；缺省空实现，观测方按需覆盖）。</summary>
    void OnLatency(string messageId, double milliseconds) { }

    /// <summary>队列深度采样（作用域维度；缺省空实现）。</summary>
    void OnQueueDepth(string scope, int depth) { }
}
