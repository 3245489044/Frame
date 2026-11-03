using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Share.Conversation.Ports;

/// <summary>归拢命令判定（受控词；禁以自由文本表达成败）。</summary>
public enum TaskHistoryVerdict
{
    /// <summary>已生效（边已写入）。</summary>
    Applied,

    /// <summary>被拒绝（前置判据不满足；边未写）。</summary>
    Rejected,

    /// <summary>能力未装配（端口/命令面缺失；不冒充失败也不冒充成功）。</summary>
    Unavailable,
}

/// <summary>归拢命令结果（结构事实；<see cref="Detail"/> 只作补充，不参与判定）。</summary>
public sealed record TaskHistoryResult(
    TaskHistoryVerdict Verdict,
    string Code,
    string Detail,
    string? CurrentParent = null)
{
    public bool Ok => Verdict == TaskHistoryVerdict.Applied;

    public static TaskHistoryResult Applied(string detail, string? currentParent = null)
        => new(TaskHistoryVerdict.Applied, "history.attach.applied", detail, currentParent);

    public static TaskHistoryResult Rejected(string code, string detail, string? currentParent = null)
        => new(TaskHistoryVerdict.Rejected, code, detail, currentParent);

    public static TaskHistoryResult Unavailable(string detail)
        => new(TaskHistoryVerdict.Unavailable, "history.not-assembled", detail);
}

/// <summary>
/// 任务↔历史归拢唯一命令端口（DUP-6 / ERR-5 收口）：
/// 显式父优先（缺省不猜选父历史）、自环拒绝、单父预检（已属其它历史 ⇒ <c>Rejected(history.already-owned)</c> 并回显现父）。
/// <para>创建 task 本身不归拢；任何面上的归拢都必须经本端口，禁各处再写第二套归拢代码。</para>
/// </summary>
public interface ITaskHistoryCommandPort
{
    /// <summary>把 task 归拢到显式历史；<paramref name="explicitHistoryId"/> 为空 ⇒ 拒绝（不猜选默认父）。</summary>
    Task<TaskHistoryResult> AttachAsync(string taskKey, string? explicitHistoryId, CancellationToken ct = default);

    /// <summary>
    /// **创建期批量归拢**（唯一例外入口）：task 尚不存在/刚建，允许一次归拢到多个父历史（M:N）。
    /// <para>
    /// 归拢期（<see cref="AttachAsync"/>）是单父不变量；创建期批量是创建命令的一部分，二者语义不同，
    /// 故以独立方法显式分模式（禁以「再挂一个父」绕过单父判据）。
    /// 判据：空集/自环拒绝；任一现父不在请求集中 ⇒ <c>Rejected(history.already-owned)</c> 带现父；
    /// 已属于请求集的父幂等跳过。
    /// </para>
    /// </summary>
    Task<TaskHistoryResult> AttachForCreationAsync(
        string taskKey,
        IReadOnlyList<string> historyIds,
        string actor = "port",
        CancellationToken ct = default);

    /// <summary>解除 task 与指定历史的归拢边（先证归属，未归拢不报成功）。</summary>
    Task<TaskHistoryResult> DetachAsync(string taskKey, string historyId, CancellationToken ct = default);
}
