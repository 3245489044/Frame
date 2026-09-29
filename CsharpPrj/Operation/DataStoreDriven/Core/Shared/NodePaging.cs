using System;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 节点库 afterId 游标分页统一骨架：收敛 <see cref="NodeGraphReconciler"/> / <see cref="ReferenceGrounding"/>
/// 重复的「while(true){ ListAsync(afterId, limit) … afterId=batch[^1].Id }」分页循环（RedundancyElimination）。
/// </summary>
internal static class NodePaging
{
    /// <summary>对指定 NodeType（可选 threadId 过滤）逐节点回调一次；空批结束分页。</summary>
    public static async Task ForEachAsync(INodeStore nodes, string nodeType, string? threadId,
        Func<NodeRecord, CancellationToken, Task> process, CancellationToken ct, int batchSize = 100)
    {
        long? afterId = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await nodes.ListAsync(nodeType, threadId, afterId, batchSize, ct).ConfigureAwait(false);
            if (batch.Count == 0) break;
            foreach (var node in batch)
            {
                if (node is null) continue;
                await process(node, ct).ConfigureAwait(false);
            }
            afterId = batch[^1].Id;
        }
    }
}
