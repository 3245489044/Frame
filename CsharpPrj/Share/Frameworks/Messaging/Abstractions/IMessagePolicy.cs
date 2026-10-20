namespace Share.Messaging;

/// <summary>策略端口：分类/上限/隔离/TTL 全部外置（声明驱动实现由消费方提供）。</summary>
public interface IMessagePolicy
{
    /// <summary>按信封分类（实现不得依赖时钟/IO）。</summary>
    DeliveryClass Classify(MessageEnvelope message);

    /// <summary>按种类分类（读/确认等无信封场景用同一张表）。</summary>
    DeliveryClass Classify(MessageKind kind);

    /// <summary>最大委托深度。</summary>
    int MaxHop { get; }

    /// <summary>最大扇出。</summary>
    int MaxFanout { get; }

    /// <summary>缺省存活秒数。</summary>
    int DefaultTtlSeconds { get; }

    /// <summary>最大队列深度。</summary>
    int MaxQueueDepth { get; }

    /// <summary>作用域是否被授权（隔离边界）。</summary>
    bool IsScopeAuthorized(MessageEnvelope message);
}
