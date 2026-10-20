namespace Share.Messaging;

/// <summary>投递画像：由策略表决定，不由调用方决定。</summary>
public enum DeliveryClass
{
    /// <summary>落盘、可重放、可跨进程（Command/Result）。</summary>
    Durable,

    /// <summary>合并/攒批/条件触发（Event）。</summary>
    Coalesced,

    /// <summary>进程内同步/异步推送（Control、低延迟命令）。</summary>
    Direct,

    /// <summary>仅追加、可投影（Context/观测）。</summary>
    Stream,

    /// <summary>控制面（pause/resume/cancel）。</summary>
    Control,
}
