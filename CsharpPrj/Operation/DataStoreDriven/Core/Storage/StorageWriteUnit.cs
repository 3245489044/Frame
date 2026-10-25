using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 跨库写单元（双库唯一写序：<b>先 SQL 后图</b>，禁反向；删除的最终动作除外——见方案 §7.3）。
/// <para>
/// 语义：<see cref="Sql"/> 为权威实体写（可含同库多语句/事务）；<see cref="Graph"/> 为派生索引写（幂等），
/// 失败入 Outbox 由对账/重放补齐；<see cref="CompensateGraph"/> 缺省由调用方不提供（= 不补偿，交重放）。
/// </para>
/// </summary>
/// <param name="Name">单元名（可见化/审计用，如 <c>task.enqueue</c>）。</param>
/// <param name="IdempotencyKey">重放幂等键（如 <c>task:&lt;key&gt;:enqueue:v1</c>）。</param>
/// <param name="Sql">权威实体写。</param>
/// <param name="Graph">派生关系写（可空 = 无图侧动作）。</param>
/// <param name="CompensateGraph">可选图侧回滚。</param>
/// <param name="PayloadJson">重放所需最小描述（入 Outbox；缺省空）。</param>
public sealed record StorageWriteUnit(
    string Name,
    string IdempotencyKey,
    Func<CancellationToken, Task> Sql,
    Func<CancellationToken, Task>? Graph = null,
    Func<CancellationToken, Task>? CompensateGraph = null,
    string PayloadJson = "");

/// <summary>
/// 写单元执行结果（事实可见化：SQL 是否提交 / 图侧是否应用 / 是否入 Outbox）。
/// </summary>
/// <param name="Name">单元名。</param>
/// <param name="SqlCommitted">权威写已提交。</param>
/// <param name="GraphApplied">派生写已应用（无图侧动作 ⇒ true）。</param>
/// <param name="OutboxKey">入 Outbox 的幂等键（未入 ⇒ null）。</param>
/// <param name="Reasons">结构事实原因（异常类型 + 消息；不含叙述）。</param>
public sealed record WriteUnitResult(
    string Name,
    bool SqlCommitted,
    bool GraphApplied,
    string? OutboxKey,
    IReadOnlyList<string> Reasons)
{
    /// <summary>整体成功（SQL 提交 ∧ 图已应用）。</summary>
    public bool Succeeded => SqlCommitted && GraphApplied;
}

/// <summary>
/// 跨库写单元执行器（唯一实现 = <see cref="CompositeWriteUnit"/>）。
/// </summary>
public interface ICompositeWriteUnit
{
    /// <summary>按固定序执行：SQL → Graph；图失败入 Outbox 并返回 <c>SqlCommitted=true</c>。</summary>
    Task<WriteUnitResult> ExecuteAsync(StorageWriteUnit unit, CancellationToken ct = default);

    /// <summary>重放 Outbox（启动 / 批次后）；返回本次成功重放条数。</summary>
    Task<int> DrainOutboxAsync(int maxItems = 100, CancellationToken ct = default);

    /// <summary>登记重放工厂（单元名 → 由 PayloadJson 重建可执行单元）；未登记名的条目重放时标 Abandoned。</summary>
    void RegisterReplay(string unitName, Func<OutboxEntryFactory, StorageWriteUnit?> factory);
}

/// <summary>重放工厂入参（Outbox 条目的结构事实）。</summary>
/// <param name="PayloadJson">条目载荷。</param>
/// <param name="IdempotencyKey">条目幂等键。</param>
public readonly record struct OutboxEntryFactory(string PayloadJson, string IdempotencyKey);
