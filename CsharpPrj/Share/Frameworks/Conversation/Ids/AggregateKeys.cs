using System;

namespace Share.Conversation.Ids;

/// <summary>
/// 历史根键（强类型）—— 与 TaskKey / SessionKey 分离，禁止隐式字符串互转。
/// 线格式保持原样（历史根 Id 即图端点反解值），构造时只做非空校验，不改变既有键文法。
/// </summary>
public readonly record struct ContainerKey
{
    public string Value { get; }

    private ContainerKey(string value) => Value = value;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static ContainerKey Create(string? value)
        => TryCreate(value, out var key)
            ? key
            : throw new ArgumentException("ContainerKey 不能为空。", nameof(value));

    public static bool TryCreate(string? value, out ContainerKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        key = new ContainerKey(value.Trim());
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>任务键（强类型）—— 工作单元身份，可与历史根多对多。</summary>
public readonly record struct TaskKey
{
    public string Value { get; }

    private TaskKey(string value) => Value = value;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static TaskKey Create(string? value)
        => TryCreate(value, out var key)
            ? key
            : throw new ArgumentException("TaskKey 不能为空。", nameof(value));

    public static bool TryCreate(string? value, out TaskKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        key = new TaskKey(value.Trim());
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>会话实例键（强类型）—— 一次执行上下文，不等于历史根，也不必然等于任务键。</summary>
public readonly record struct SessionKey
{
    public string Value { get; }

    private SessionKey(string value) => Value = value;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static SessionKey Create(string? value)
        => TryCreate(value, out var key)
            ? key
            : throw new ArgumentException("SessionKey 不能为空。", nameof(value));

    public static bool TryCreate(string? value, out SessionKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key = default;
            return false;
        }

        key = new SessionKey(value.Trim());
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>会话归属种类：历史主循环会话 / 任务会话。</summary>
public enum SessionOwnerKind
{
    History = 0,
    Task = 1,
}

/// <summary>
/// 会话归属（强类型）—— 修正「session 必属 task」的口径：
/// 空历史发送需要 <see cref="SessionOwnerKind.History"/> 主循环会话，成员/孤立任务会话属 <see cref="SessionOwnerKind.Task"/>。
/// </summary>
public readonly record struct SessionOwner(SessionOwnerKind Kind, string Key)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Key);

    public static SessionOwner OfHistory(ContainerKey key) => new(SessionOwnerKind.History, key.Value);
    public static SessionOwner OfHistory(string key) => new(SessionOwnerKind.History, ContainerKey.Create(key).Value);
    public static SessionOwner OfTask(TaskKey key) => new(SessionOwnerKind.Task, key.Value);
    public static SessionOwner OfTask(string key) => new(SessionOwnerKind.Task, TaskKey.Create(key).Value);

    public override string ToString() => $"{Kind}:{Key}";
}
