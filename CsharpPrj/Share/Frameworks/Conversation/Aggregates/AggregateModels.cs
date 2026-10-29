using System;
using Share.Conversation.Ids;

namespace Share.Conversation.Aggregates;

/// <summary>会话角色（创建时确定，之后不可变）。</summary>
public enum SessionRole
{
    ContainerRoot = 0,
    TaskMember = 1,
    StandaloneTask = 2,
    InternalRound = 3,
}

/// <summary>创建意图：自动派生一律拒绝；用户显式/系统命令才可创建。</summary>
public enum CreationIntent
{
    Automatic = 0,
    UserRequested = 1,
    SystemExplicit = 2,
}

/// <summary>历史根种类（仅展示/审计语义，不参与身份判定）。</summary>
public enum ContainerKind
{
    User = 0,
    Imported = 1,
    System = 2,
}

/// <summary>历史根注册表记录 —— 只承载聚合根身份与展示名，不承载会话日志。</summary>
public sealed record ContainerRoot(
    ContainerKey Key,
    string Name,
    ContainerKind Kind,
    string CreatedBy,
    DateTimeOffset CreatedAt)
{
    public static ContainerRoot Create(ContainerKey key, string? name, ContainerKind kind, string createdBy)
        => new(key, string.IsNullOrWhiteSpace(name) ? key.Value : name.Trim(), kind,
            string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(), DateTimeOffset.UtcNow);

    public ContainerRoot Rename(string? name)
        => this with { Name = string.IsNullOrWhiteSpace(name) ? Key.Value : name.Trim() };
}

/// <summary>会话实例记录 —— 属于一个 owner（历史根或任务），不表示历史实体本身。</summary>
public sealed record SessionRecord(
    SessionKey Key,
    SessionOwner Owner,
    SessionRole Role,
    string Agent,
    DateTimeOffset CreatedAt)
{
    public static SessionRecord Create(SessionKey key, SessionOwner owner, SessionRole role, string? agent)
        => new(key, owner, role, string.IsNullOrWhiteSpace(agent) ? "history" : agent.Trim(), DateTimeOffset.UtcNow);
}

/// <summary>创建授权结果（拒绝时必须带可见原因）。</summary>
public sealed record CreationDecision(bool Allowed, string Reason)
{
    public static CreationDecision Allow(string reason = "allowed") => new(true, reason);
    public static CreationDecision Reject(string reason) => new(false, reason);
}

/// <summary>迁移期旧根分类器端口：Frame 不内置任何 Agent 键前缀规则。</summary>
public interface ILegacyContainerClassifier
{
    /// <summary>该旧 ur_histories 行是否可视为历史根（迁移期兼容读）。</summary>
    bool IsLegacyRoot(string key);

    /// <summary>旧行迁移角色（用于 ur_sessions）。</summary>
    SessionRole ResolveRole(string key);
}
