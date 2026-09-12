using System;
using System.Collections.Generic;
using System.Text.Json;
using Share.TaskStatusPool.Loops;

namespace Operation.HierarchyMethod.Nodes.WritePlan;

/// <summary>
/// 写介质（唯一写权的物理分类）：一个介质对应一组唯一写端口。
/// Sqlite = agent.db（ur_nodes / ur_tasks / ur_histories）；Graph = 引用/归属边；ConfigFile = agent-config.json。
/// </summary>
public enum WriteMedia
{
    /// <summary>agent.db（SQLite 单写通道）。</summary>
    Sqlite,

    /// <summary>引用/归属边（ReferenceGraphCoordinator 唯一 owner）。</summary>
    Graph,

    /// <summary>配置文件（agent-config.json）。</summary>
    ConfigFile,
}

/// <summary>
/// 写集合常量（介质内的目标集合判别）。写门按 (Media, Collection) 分派到唯一写端口；
/// 删除语义用独立集合名表达（<c>*.delete</c>），避免在载荷里再造一套 op 判别式。
/// </summary>
public static class WriteCollections
{
    public const string Nodes = "ur_nodes";
    public const string NodesDelete = "ur_nodes.delete";
    public const string Tasks = "ur_tasks";
    public const string Histories = "ur_histories";
    public const string HistoriesDelete = "ur_histories.delete";
    public const string Edges = "edges";
}

/// <summary>
/// 单个写步骤（声明层只声明「目标状态」，多步展开由写计划承担）。
/// 对齐设计 §4.2 ③：<c>WriteWork { Media, Collection, Key, Value, Deps }</c>。
/// </summary>
/// <param name="Id">计划内唯一 Id（依赖/短路引用键）。</param>
/// <param name="Media">写介质。</param>
/// <param name="Collection">介质内目标集合（见 <see cref="WriteCollections"/>）。</param>
/// <param name="Key">主键（nodeKey | taskKey | historyId | relationId）。</param>
/// <param name="Payload">载荷（形态由介质处理器解释）。</param>
/// <param name="DependsOn">前序依赖的 Id 集合（拓扑序）；null = 无依赖。</param>
/// <param name="LlmFree">本步骤是否 LLM-free（装配期断言输入：会话类计划必须全 true）。</param>
public sealed record WriteWork(
    string Id,
    WriteMedia Media,
    string Collection,
    string? Key,
    JsonElement Payload,
    IReadOnlyList<string>? DependsOn = null,
    bool LlmFree = true)
{
    /// <summary>依赖集（去 null，空集非 null）。</summary>
    public IReadOnlyList<string> Dependencies => DependsOn ?? Array.Empty<string>();
}

/// <summary>
/// 计划短路：把「前提已满足 → 跳过该步」显式声明为数据（对齐设计 §4.2 S2 守卫已装配 → 跳过 addResource）。
/// 被短路步骤视为**已满足**，其下游依赖仍继续执行。
/// </summary>
public sealed record PlanShortCircuit(string WorkId, string Reason);

/// <summary>
/// 写计划（唯一编排点的产物）：有序步骤 + 显式短路声明。纯数据——可打印、可对账、可测试。
/// </summary>
public sealed record WritePlan(
    string Id,
    IReadOnlyList<WriteWork> Steps,
    IReadOnlyList<PlanShortCircuit>? ShortCircuits = null)
{
    /// <summary>短路声明集（去 null，空集非 null）。</summary>
    public IReadOnlyList<PlanShortCircuit> Skips => ShortCircuits ?? Array.Empty<PlanShortCircuit>();
}

/// <summary>
/// 写执行结果：applied/rejected 二态 + 分类错误 + 已应用/已短路清单（供对账与诊断）。
/// </summary>
public sealed record WriteOutcome(
    bool Applied,
    string PlanId,
    IReadOnlyList<string> AppliedWorkIds,
    IReadOnlyList<string> SkippedWorkIds,
    string? RejectedAt,
    ErrorClass? Error,
    string? Reason)
{
    /// <summary>全步骤成功。</summary>
    public static WriteOutcome Ok(string planId, IReadOnlyList<string> applied, IReadOnlyList<string> skipped)
        => new(true, planId, applied, skipped, null, null, null);

    /// <summary>计划被拒绝（任一步失败即整体拒绝，前序已应用步骤走补偿）。</summary>
    public static WriteOutcome Rejected(
        string planId, IReadOnlyList<string> applied, IReadOnlyList<string> skipped,
        string? rejectedAt, ErrorClass error, string? reason)
        => new(false, planId, applied, skipped, rejectedAt, error, reason);
}
