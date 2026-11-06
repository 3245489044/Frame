using System;

namespace Modules.Node.Relations;

/// <summary>边写入请求（键/端点纯文法，不含存储负载；负载由 <see cref="IEdgeWriteSink{TPayload}"/> 解释）。</summary>
public readonly record struct EdgeWriteRequest(
    string EdgeId,
    string SourceKey,
    string TargetKey,
    string RefType,
    string? LegacyEdgeId = null);

/// <summary>边写入结局：<see cref="Written"/> = 已落 durable + 邻接；<see cref="Skipped"/> = 准入拒绝（调用方保留原因）。</summary>
public enum EdgeWriteOutcomeKind
{
    Written,
    Skipped
}

/// <summary>
/// 边写入存储端口：把「durable 行 + 热邻接」的原子步骤抽象为最小能力；
/// 存储实现（LiteDB/内存/远端）各自适配，写序不重复。
/// </summary>
public interface IEdgeWriteSink<in TPayload>
{
    void DeleteDurableEdge(string edgeId);

    bool ContainsGraphEdge(string edgeId);

    void RemoveGraphEdge(string edgeId);

    void InsertEdge(EdgeWriteRequest request, TPayload payload);

    void AddAdjacency(EdgeWriteRequest request, TPayload payload);
}

/// <summary>
/// 边写入协调器（Modules B-1 单源）：准入钩子 → 先删后加（幂等 upsert，避免重复行）
/// → 旧端点形态同键清理（文法升级后同一关系只能有一行）→ durable 落行 → 热邻接去重添加。
/// <para>
/// 职责边界：不做策略判定（由 <paramref name="admit"/> 注入）、不解释负载、不持有锁；
/// 在同一写门（调用方 Mutate/事务）内调用，保证与墓碑/端点登记的原子性。
/// </para>
/// </summary>
public static class EdgeWriteCoordinator
{
    /// <summary>执行一次边写入。<paramref name="admit"/> 返回 false → <see cref="EdgeWriteOutcomeKind.Skipped"/>（不触碰存储）。</summary>
    public static EdgeWriteOutcomeKind Write<TPayload>(
        EdgeWriteRequest request,
        TPayload payload,
        IEdgeWriteSink<TPayload> sink,
        Func<EdgeWriteRequest, bool>? admit = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (admit is not null && !admit(request))
            return EdgeWriteOutcomeKind.Skipped;

        // durable：先删后加（幂等 upsert）。
        sink.DeleteDurableEdge(request.EdgeId);

        // 旧端点形态（无 @location）的同一条边一并清除，避免文法升级后同一关系存在两行。
        if (!string.IsNullOrEmpty(request.LegacyEdgeId)
            && !string.Equals(request.LegacyEdgeId, request.EdgeId, StringComparison.Ordinal))
        {
            sink.DeleteDurableEdge(request.LegacyEdgeId);
            if (sink.ContainsGraphEdge(request.LegacyEdgeId))
                sink.RemoveGraphEdge(request.LegacyEdgeId);
        }

        sink.InsertEdge(request, payload);

        if (!sink.ContainsGraphEdge(request.EdgeId))
            sink.AddAdjacency(request, payload);

        return EdgeWriteOutcomeKind.Written;
    }
}
