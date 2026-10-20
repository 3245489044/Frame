namespace Share.Messaging;

/// <summary>消息种类（开放受控词）：未知种类 → 死信（禁静默丢弃）。</summary>
public enum MessageKind
{
    /// <summary>命令（委托/下达）：必须有 ReplyTo（闭包凭证）。</summary>
    Command,

    /// <summary>结果（结论/失败诊断）：终态，单次有效。</summary>
    Result,

    /// <summary>事件（进度/状态变化）：可合并，不要求回执。</summary>
    Event,

    /// <summary>上下文（共享文档块/仓库地图）：内容寻址，幂等。</summary>
    Context,

    /// <summary>控制（pause/resume/cancel/ack）：单调，同 scope 有序。</summary>
    Control,
}
