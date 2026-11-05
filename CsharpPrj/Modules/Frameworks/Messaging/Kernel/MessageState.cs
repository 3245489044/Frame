namespace Share.Messaging;

/// <summary>
/// 消息生命周期（唯一状态机）：单调迁移，终态不可复活。
/// <para>适配器只允许把各自旧状态映射到本枚举，禁新增第三套状态枚举。</para>
/// </summary>
public enum MessageState
{
    /// <summary>已构造，尚未发布。</summary>
    Created,

    /// <summary>已发布（落传输/入队/预分配）。</summary>
    Published,

    /// <summary>已被消费者认领。</summary>
    Claimed,

    /// <summary>已被消费（处理中或已产出观测）。</summary>
    Consumed,

    /// <summary>已确认（终态）。</summary>
    Acked,

    /// <summary>失败（可重试；超限转 <see cref="DeadLetter"/>）。</summary>
    Failed,

    /// <summary>过期（终态）。</summary>
    Expired,

    /// <summary>死信（终态，不参与重试）。</summary>
    DeadLetter,

    /// <summary>取消（终态）。</summary>
    Cancelled,
}
