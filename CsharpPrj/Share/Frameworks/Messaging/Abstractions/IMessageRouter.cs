namespace Share.Messaging;

/// <summary>唯一选路点：策略分类 → 投递画像 → 传输实现。</summary>
public interface IMessageRouter
{
    /// <summary>分类（与 <see cref="IMessagePolicy.Classify(MessageEnvelope)"/> 同源）。</summary>
    DeliveryClass Classify(MessageEnvelope message);

    /// <summary>解析传输；未装配画像 ⇒ 抛 <see cref="MessageTransportNotWiredException"/>（禁静默回退）。</summary>
    IMessageTransport Resolve(MessageEnvelope message);
}
