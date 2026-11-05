namespace Modules.KeySpace.Dispatch;

/// <summary>休眠激活触发源（dormant 必须携带，缺项 = 非法入池）。</summary>
public enum ActivationTrigger
{
    /// <summary>容器成员集出现该目标时自动激活。</summary>
    MemberAttached,

    /// <summary>仅显式 Activate 调用（不自动超时）。</summary>
    Explicit,

    /// <summary>调度器按优先级激活。</summary>
    Scheduler,
}

/// <summary>
/// 激活条件（生命周期契约）：触发源 + 超时 + 最大重试。
/// <para>语义：<see cref="ActivationTrigger.Explicit"/> 不自动超时；其余触发源超时 → failed。</para>
/// </summary>
public readonly record struct ActivationCondition(
    ActivationTrigger Trigger,
    int? TimeoutSeconds,
    int MaxRetries)
{
    /// <summary>结构合法性：MemberAttached/Scheduler 必须有正超时；重试非负。</summary>
    public bool IsValid
        => MaxRetries >= 0
           && (Trigger == ActivationTrigger.Explicit
               || TimeoutSeconds is > 0);

    /// <summary>是否自动超时（仅非 Explicit）。</summary>
    public bool AutoTimeout
        => Trigger != ActivationTrigger.Explicit && TimeoutSeconds is > 0;

    /// <summary>缺省显式条件（观察者/手工激活）。</summary>
    public static ActivationCondition Explicit(int? timeoutSeconds = null, int maxRetries = 0)
        => new(ActivationTrigger.Explicit, timeoutSeconds, maxRetries);

    /// <summary>缺省成员挂载条件（超时必填且为正）。</summary>
    public static ActivationCondition MemberAttached(int timeoutSeconds, int maxRetries = 0)
        => new(ActivationTrigger.MemberAttached, timeoutSeconds, maxRetries);
}
