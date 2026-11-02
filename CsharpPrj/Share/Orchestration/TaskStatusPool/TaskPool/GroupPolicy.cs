using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>组结果交付语义（通用，无领域词）：await = 等组结算；detach = 只回受理回执。</summary>
public enum GroupWaitMode
{
    Await = 0,
    Detach = 1,
}

/// <summary>
/// 组策略单源（通用）：并发上限、成员上限、完成判定、等待语义、组级超时、探针缺失策略。
/// <para>领域侧只做声明映射，不得在框架层出现领域词（history/task_owns_task 等）。</para>
/// </summary>
public sealed class GroupPolicy
{
    /// <summary>并发处理上限（与容量门同一真值；默认 1 = 串行）。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>组成员上限（缺省 8，硬上界 32；提交期校验，超限拒收不截断）。</summary>
    public int MaxMembers { get; set; } = 8;

    /// <summary>组完成判定缺省（all_of | quorum | fail_fast 的强类型映射）。</summary>
    public TaskGroupCompletion DefaultCompletion { get; set; } = TaskGroupCompletion.AllOf;

    /// <summary>quorum 缺省需成功数；0 = 按成员数取简单多数。</summary>
    public int DefaultQuorum { get; set; }

    /// <summary>组结果交付语义（await | detach）。</summary>
    public GroupWaitMode WaitMode { get; set; } = GroupWaitMode.Await;

    /// <summary>组级预算（毫秒）：0 = 未声明（不做看门狗）；&gt;0 = 超时结算 GROUP_TIMEOUT 并取消成员。</summary>
    public int GroupTimeoutMs { get; set; }

    /// <summary>组内依赖探针缺失时是否允许成组（默认 false = fail-closed）。</summary>
    public bool AllowUnverifiedGroups { get; set; }
}

/// <summary>并发容量来源（单源）：声明上限 + 实时可用 slot。池与门禁读同一实例，禁止两处配置。</summary>
public interface IGroupCapacitySource
{
    /// <summary>声明上限。</summary>
    int MaxConcurrency { get; }

    /// <summary>实时可用 slot。</summary>
    int AvailableSlots { get; }
}

/// <summary>组级审计来源（拉取式）：结算时按 GroupKey 汇总节点租约命中计数。</summary>
public interface IGroupAuditSource
{
    /// <summary>取该组的节点复用统计；未知组返回 (0,0,0)。</summary>
    NodeReuseStat Snapshot(string groupKey);
}

/// <summary>组生命周期持久化（P0-3）：终态组可跨重启按 GroupKey 取回。</summary>
public interface IGroupStore
{
    /// <summary>写组终态（幂等：同组键后写覆盖前写）。</summary>
    Task SaveAsync(TaskGroupResult result, CancellationToken ct = default);

    /// <summary>按组键取回已结算组；无记录返回 null。</summary>
    Task<TaskGroupResult?> TryGetAsync(string groupKey, CancellationToken ct = default);
}

/// <summary>组受控诊断码（结构码，供调用方回落判定；新增码只可追加）。</summary>
public static class GroupErrorCode
{
    public const string KeyEmpty = "GROUP_KEY_EMPTY";
    public const string Empty = "GROUP_EMPTY";
    public const string TooManyMembers = "GROUP_TOO_MANY_MEMBERS";
    public const string MemberCountMismatch = "GROUP_MEMBER_COUNT_MISMATCH";
    public const string DuplicateMember = "GROUP_DUPLICATE_MEMBER";
    public const string Nested = "GROUP_NESTED";
    public const string AlreadyExists = "GROUP_ALREADY_EXISTS";
    public const string AlreadySettled = "GROUP_ALREADY_SETTLED";
    public const string MemberAlreadyEnrolled = "GROUP_MEMBER_ALREADY_ENROLLED";
    public const string HasDependency = "GROUP_HAS_DEPENDENCY";
    public const string ProbeMissing = "GROUP_DEPENDENCY_PROBE_MISSING";
    public const string ProbeReadFailed = "GROUP_PROBE_READ_FAILED";
    public const string MemberNotFound = "GROUP_MEMBER_NOT_FOUND";
    public const string RaceDuplicate = "GROUP_RACE_DUPLICATE";
    public const string EnqueueFailed = "GROUP_ENQUEUE_FAILED";
    public const string NotFound = "GROUP_NOT_FOUND";
    public const string Timeout = "GROUP_TIMEOUT";
}
