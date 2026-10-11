using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Storage;

namespace UniversalWorkflowFramework.StorageFramework.Batch;

/// <summary>
/// **LiteDB 图库批边端口**（引擎级、零领域）：把「边 id 集 × 删除」统一成批，逐项
/// <see cref="BatchItemOutcome"/>（禁整批静默）。图库（graph-log.db）批边 = 本端口唯一构建点。
/// </summary>
public interface ILiteEdgeBatchStore
{
    /// <summary>批删边（按边 id 集）。</summary>
    Task<BatchResult> RemoveEdgesAsync(IReadOnlyList<string> edgeIds, CancellationToken ct = default);
}

/// <summary>
/// <see cref="ILiteEdgeBatchStore"/> 的 **LiteDB 默认实现**：包 <see cref="ILiteDBPersistence{TKey,TNodeData,TEdgeData}"/>
/// 的逐边原语，收敛「循环逐键删边」为单一构建点。
/// </summary>
public sealed class LiteDbEdgeBatchStore<TKey, TNodeData, TEdgeData> : ILiteEdgeBatchStore
    where TKey : notnull
    where TNodeData : class
    where TEdgeData : class
{
    private readonly ILiteDBPersistence<TKey, TNodeData, TEdgeData> _persistence;

    /// <param name="persistence">LiteDB 持久化实例（图库）。</param>
    public LiteDbEdgeBatchStore(ILiteDBPersistence<TKey, TNodeData, TEdgeData> persistence)
        => _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

    /// <inheritdoc />
    public async Task<BatchResult> RemoveEdgesAsync(IReadOnlyList<string> edgeIds, CancellationToken ct = default)
    {
        if (edgeIds is null || edgeIds.Count == 0)
        {
            return BatchResult.Empty;
        }

        var items = new List<BatchItemOutcome>(edgeIds.Count);
        foreach (var id in edgeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                items.Add(BatchItemOutcome.Fail(id ?? string.Empty, BatchItemStatus.Rejected, "空边 id"));
                continue;
            }

            try
            {
                if (!await _persistence.ContainsEdgeAsync(id).ConfigureAwait(false))
                {
                    items.Add(BatchItemOutcome.Fail(id, BatchItemStatus.NotFound, null));
                    continue;
                }

                await _persistence.RemoveEdgeAsync(id).ConfigureAwait(false);
                items.Add(BatchItemOutcome.Ok(id));
            }
            catch (Exception ex)
            {
                items.Add(BatchItemOutcome.Fail(id, BatchItemStatus.Rejected, ex.Message));
            }
        }

        return new BatchResult(items);
    }
}
