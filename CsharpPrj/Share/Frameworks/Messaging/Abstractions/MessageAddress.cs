namespace Share.Messaging;

/// <summary>地址种类（开放受控词；未知种类由适配器解析失败 → 死信）。</summary>
public enum MessageAddressKind
{
    /// <summary>执行体（角色/进程/智能体）。</summary>
    Actor,

    /// <summary>任务（任务池键）。</summary>
    Task,

    /// <summary>会话（会话键）。</summary>
    Session,

    /// <summary>角色（标签 → 执行腿）。</summary>
    Role,

    /// <summary>主题（订阅表，有界展开）。</summary>
    Topic,

    /// <summary>广播（受 MaxFanout 约束）。</summary>
    Broadcast,
}

/// <summary>消息地址：<c>(Kind, Id, Scope?)</c>；<c>Scope</c> 为隔离边界。</summary>
public readonly record struct MessageAddress(string Kind, string Id, string? Scope = null)
{
    /// <summary>是否广播地址。</summary>
    public bool IsBroadcast
        => string.Equals(Kind, nameof(MessageAddressKind.Broadcast), StringComparison.OrdinalIgnoreCase);

    /// <summary>规范文本（诊断/日志用）。</summary>
    public override string ToString()
        => string.IsNullOrWhiteSpace(Scope) ? $"{Kind}:{Id}" : $"{Kind}:{Id}@{Scope}";
}
