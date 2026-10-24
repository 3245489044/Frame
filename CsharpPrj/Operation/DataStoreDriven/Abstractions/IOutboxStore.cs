using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>Outbox 条目状态（受控词）。</summary>
public static class OutboxStates
{
    public const string Pending = "Pending";
    public const string Applied = "Applied";
    public const string Abandoned = "Abandoned";
}

/// <summary>
/// 跨库写单元的重放条目（C3：图失败 ⇒ Outbox，SQL 结果不回收）。
/// <para>与它修复的副作用同库（图库集合 <c>ur_write_outbox</c>）⇒ 重放不跨库事务；图库丢失时由全量对账重建。</para>
/// </summary>
/// <param name="IdempotencyKey">幂等键（同键重复入队即覆盖）。</param>
/// <param name="UnitName">单元名（重放工厂按名解析）。</param>
/// <param name="PayloadJson">重放所需最小描述（结构事实）。</param>
/// <param name="Attempts">已重放次数。</param>
/// <param name="LastError">最近失败原因（结构事实，非叙述）。</param>
/// <param name="State">条目状态（<see cref="OutboxStates"/>）。</param>
/// <param name="FirstFailedAt">首次失败时间（UTC ISO-8601）。</param>
public sealed record OutboxEntry(
    string IdempotencyKey,
    string UnitName,
    string PayloadJson,
    int Attempts,
    string? LastError,
    string State,
    string FirstFailedAt);

/// <summary>
/// Outbox 存储端口（唯一定义；实现 = 图库 owner 内部集合，禁另开第二 LiteDB 句柄 ⇒ 保持库级单写者）。
/// </summary>
public interface IOutboxStore
{
    /// <summary>幂等入队（同 <paramref name="entry"/>.IdempotencyKey 覆盖为 Pending）。</summary>
    Task UpsertPendingAsync(OutboxEntry entry, CancellationToken ct = default);

    /// <summary>列 Pending 条目（按首次失败时间升序）。</summary>
    Task<IReadOnlyList<OutboxEntry>> ListPendingAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>标记已应用（保留行，供审计；不计入 Pending）。</summary>
    Task MarkAppliedAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>标记放弃（附结构事实原因；Abandoned 必须可见）。</summary>
    Task MarkAbandonedAsync(string idempotencyKey, string reason, CancellationToken ct = default);

    /// <summary>Pending / Abandoned 计数（启动轻检的量级判据）。</summary>
    Task<(int Pending, int Abandoned)> CountAsync(CancellationToken ct = default);
}
