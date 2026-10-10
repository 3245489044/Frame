using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 引用关系写外盒（G8）：内存待写队列 + 幂等冲刷（经唯一写边 owner <see cref="Graph.ReferenceGraphCoordinator"/>）。
/// 与实体写同批登记；跨介质失败时经 <see cref="FlushAsync"/> 重放补偿（替代从节点重建）。
/// </summary>
public sealed class ReferenceOutbox : IReferenceOutbox
{
    private readonly List<StaticReference> _pending = new();
    private readonly Graph.ReferenceGraphCoordinator _graph;

    public ReferenceOutbox(Graph.ReferenceGraphCoordinator graph)
        => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

    public Task EnqueueAsync(StaticReference edge, CancellationToken ct = default)
    {
        if (edge is not null) _pending.Add(edge);
        return Task.CompletedTask;
    }

    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var flushed = 0;
        foreach (var e in _pending)
        {
            if (e is null) continue;
            await _graph.AddReferenceAsync(
                new ResourceIdentifier { ResourceType = e.SourceResourceType, Id = e.SourceId },
                new ResourceIdentifier { ResourceType = e.TargetResourceType, Id = e.TargetId },
                e.RefType, e.Strength, ct).ConfigureAwait(false);
            flushed++;
        }
        _pending.Clear();
        return flushed;
    }
}
