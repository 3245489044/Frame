using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>组完成判定（C9）：任何策略都必须等全成员终态后才结算；FailFast 只是提前请求取消。</summary>
public enum TaskGroupCompletion
{
    AllOf = 0,
    Quorum = 1,
    FailFast = 2,
}

/// <summary>组成员运行态（从池状态机聚合，不新造账）。</summary>
public enum TaskGroupMemberStatus
{
    NotStarted = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Faulted = 4,
    Cancelled = 5,
}

/// <summary>组声明：唯一并发单元（C1）。成员数有界（C2），组内无依赖（C3），不可嵌套（C12）。
/// <para><paramref name="Completion"/> 为 null = 未声明（用 GroupPolicy 缺省）；显式声明（含 AllOf）一律不被缺省覆盖（ERR-06）。</para>
public sealed record TaskGroupDescriptor(
    string GroupKey,
    IReadOnlyList<string> MemberKeys,
    TaskGroupCompletion? Completion = null,
    int Quorum = 0,
    string? HistoryId = null,
    string? CorrelationId = null);

/// <summary>成员事实（池状态 + 宿主可补充的产物字段）。</summary>
public readonly record struct TaskGroupMember(
    string TaskKey,
    TaskGroupMemberStatus Status,
    string? Decision = null,
    int Rounds = 0,
    double DurationMs = 0,
    string? Diagnostic = null,
    long? OutputId = null);

/// <summary>节点复用统计（审计用；来自 NodeLease 命中计数）。</summary>
public readonly record struct NodeReuseStat(int Executed, int Waited, int Reused);

/// <summary>组结果 = 全集（C9）。Members.Count 恒等于提交数。</summary>
public sealed record TaskGroupResult(
    string GroupKey,
    TaskGroupCompletion Completion,
    TaskGroupMemberStatus Status,
    IReadOnlyList<TaskGroupMember> Members,
    NodeReuseStat NodeReuse,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

/// <summary>组受理回执：拒收返回结构码，不静默降级为串行。</summary>
public readonly record struct GroupSubmitReceipt(bool Accepted, string GroupKey, int MemberCount, string? Diagnostic)
{
    public static GroupSubmitReceipt Rejected(string groupKey, string diagnostic)
        => new(false, groupKey, 0, diagnostic);
}

/// <summary>组 API（C1 唯一实现）。不扩 ITaskPool，避免破坏既有实现/测试替身。</summary>
public interface ITaskGroupPool<TItem> where TItem : ITaskPoolItem
{
    Task<GroupSubmitReceipt> SubmitGroupAsync(TaskGroupDescriptor group, IReadOnlyList<TItem> items, CancellationToken ct = default);

    Task<TaskGroupResult> WaitGroupAsync(string groupKey, CancellationToken ct = default);

    Task<int> CancelGroupAsync(string groupKey, CancellationToken ct = default);
}

/// <summary>组内依赖探针（C3）：Agent 侧以 task_owns_task 边实现；未接线 ⇒ 保守放行并可见化诊断。</summary>
public interface ITaskGroupDependencyProbe
{
    Task<bool> HasIntraGroupDependencyAsync(IReadOnlyList<string> memberKeys, CancellationToken ct = default);
}

/// <summary>组作用域（C12）：worker 执行组成员时置位，成员内再提交组即拒收。</summary>
public static class TaskGroupScope
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CurrentGroupKey
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
