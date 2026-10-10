using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 三区数据契约承载（三库收敛：input/output/event 均入 ur_nodes，NodeType 判别，不再各自建表）。
/// 实现 <see cref="IZoneStoreReactive"/>：写经节点库（<see cref="INodeStore"/>，Sqlite ur_nodes）落为节点文档，
/// 提交即发布 <see cref="Writes"/> 信号（反应式触发）。与后端无关——节点库实现可为 Sqlite/LiteDB/内存。
/// </summary>
public sealed class NodeZoneStore : IZoneStoreReactive
{
    private readonly INodeStore _nodes;
    private readonly Subject<ZoneWriteSignal> _subject = new();

    public NodeZoneStore(INodeStore nodes)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    public IObservable<ZoneWriteSignal> Writes => _subject.AsObservable();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<long> AppendInputAsync(InputRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = await Insert(NodeTypes.Input, record.ThreadId, record.InputKey, null, record.PayloadJson, record.CreatedAt, ct).ConfigureAwait(false);
        Publish(ZoneKind.Input, id, record.ThreadId, record.InputKey);
        return id;
    }

    public async Task<long> AppendOutputAsync(OutputRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = await Insert(NodeTypes.Output, record.ThreadId, record.InputKey, record.Status, record.PayloadJson, record.CreatedAt, ct).ConfigureAwait(false);
        Publish(ZoneKind.Output, id, record.ThreadId, record.InputKey);
        return id;
    }

    public async Task<long> AppendEventAsync(EventRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = await Insert(NodeTypes.Event, record.ThreadId, record.EventKey, record.EventType, record.PayloadJson, record.CreatedAt, ct).ConfigureAwait(false);
        Publish(ZoneKind.Event, id, record.ThreadId, record.EventKey);
        return id;
    }

    public async Task<IReadOnlyList<InputRecord>> ListInputsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default)
        => (await Query(NodeTypes.Input, threadId, afterId, limit, ct).ConfigureAwait(false))
            .Select(n => new InputRecord { Id = n.Id, ThreadId = n.ThreadId, InputKey = n.Key, PayloadJson = UsharpValueJson.ToPayloadText(n.Payload), CreatedAt = n.CreatedAt })
            .ToList();

    public async Task<IReadOnlyList<OutputRecord>> ListOutputsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default)
        => (await Query(NodeTypes.Output, threadId, afterId, limit, ct).ConfigureAwait(false))
            .Select(n => new OutputRecord { Id = n.Id, ThreadId = n.ThreadId, InputKey = n.Key, Status = n.Status, PayloadJson = UsharpValueJson.ToPayloadText(n.Payload), CreatedAt = n.CreatedAt })
            .ToList();

    public async Task<IReadOnlyList<EventRecord>> ListEventsAsync(string? threadId = null, long? afterId = null, int limit = 100, CancellationToken ct = default)
        => (await Query(NodeTypes.Event, threadId, afterId, limit, ct).ConfigureAwait(false))
            .Select(n => new EventRecord { Id = n.Id, ThreadId = n.ThreadId, EventType = n.Status, EventKey = n.Key, PayloadJson = UsharpValueJson.ToPayloadText(n.Payload), CreatedAt = n.CreatedAt })
            .ToList();

    private async Task<long> Insert(string nodeType, string? threadId, string? key, string? status, string payloadJson, string? createdAt, CancellationToken ct)
    {
        var now = createdAt ?? UniversalWorkflowFramework.StorageFramework.Core.PersistenceHelper.NowIso();
        // 节点构造单一适配器：NodeKey 格式与空值归一化统一经 ZoneNodeAdapter（构造单点，对齐 SingleImplementation）。
        return await _nodes.PutAsync(ZoneNodeAdapter.Build(nodeType, threadId, key, status, payloadJson, createdAt: now), ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<NodeRecord>> Query(string nodeType, string? threadId, long? afterId, int limit, CancellationToken ct)
        => await _nodes.ListAsync(nodeType, threadId, afterId, limit, ct).ConfigureAwait(false);

    private void Publish(ZoneKind zone, long id, string? threadId, string? key)
        => ZoneSignalPublisher.Publish(_subject, "NodeZoneStore", new ZoneWriteSignal { Zone = zone, Id = id, ThreadId = threadId, Key = key });
}
