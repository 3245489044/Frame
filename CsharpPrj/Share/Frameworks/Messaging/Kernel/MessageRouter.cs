namespace Share.Messaging;

/// <summary>投递画像未装配（显式失败，禁静默回退到别的传输）。</summary>
public sealed class MessageTransportNotWiredException : InvalidOperationException
{
    /// <summary>构造。</summary>
    public MessageTransportNotWiredException(DeliveryClass delivery)
        : base($"未装配投递画像：{delivery}（{MessageDiagnostics.TransportUnwired}）。")
    {
        Delivery = delivery;
    }

    /// <summary>缺失的画像。</summary>
    public DeliveryClass Delivery { get; }

    /// <summary>诊断码。</summary>
    public string Code => MessageDiagnostics.TransportUnwired;
}

/// <summary>
/// 表驱动选路（唯一 switch 收敛在 <see cref="IMessagePolicy.Classify(MessageEnvelope)"/>）：
/// 输入 = 信封 + 策略，输出 = 传输实现；未装配 ⇒ 显式异常。
/// </summary>
public sealed class MessageRouter : IMessageRouter
{
    private readonly IMessagePolicy _policy;
    private readonly IReadOnlyDictionary<DeliveryClass, IMessageTransport> _transports;

    /// <summary>构造。</summary>
    public MessageRouter(IMessagePolicy policy, IReadOnlyDictionary<DeliveryClass, IMessageTransport> transports)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        ArgumentNullException.ThrowIfNull(transports);
        _transports = new Dictionary<DeliveryClass, IMessageTransport>(transports);
    }

    /// <summary>已装配画像。</summary>
    public IReadOnlyCollection<DeliveryClass> Wired => (IReadOnlyCollection<DeliveryClass>)_transports.Keys;

    /// <inheritdoc />
    public DeliveryClass Classify(MessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _policy.Classify(message);
    }

    /// <inheritdoc />
    public IMessageTransport Resolve(MessageEnvelope message)
    {
        var delivery = Classify(message);
        if (!_transports.TryGetValue(delivery, out var transport) || transport is null)
            throw new MessageTransportNotWiredException(delivery);
        return transport;
    }

    /// <summary>按画像解析（读/确认等无信封场景）。</summary>
    public IMessageTransport Resolve(DeliveryClass delivery)
    {
        if (!_transports.TryGetValue(delivery, out var transport) || transport is null)
            throw new MessageTransportNotWiredException(delivery);
        return transport;
    }
}
