using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using Share.Conversation;

namespace Operation.HierarchyMethod.Nodes.WritePlan;

/// <summary>
/// ur_nodes 唯一写端口处理器（集合 <see cref="WriteCollections.Nodes"/> / <see cref="WriteCollections.NodesDelete"/>）。
/// 经既有 <see cref="INodeStore"/> 单写者落库；载荷构造经唯一 Codec（<see cref="NodeOutputCodec.EncodeExternal"/>，DP-2），
/// 缺省节点形态与执行链单形对齐（NodeType=node、Status=executed）。
/// </summary>
public sealed class NodeStoreMediaHandler : WriteMediaHandlerBase
{
    private static readonly IReadOnlyList<string> OwnedCollections =
        new[] { WriteCollections.Nodes, WriteCollections.NodesDelete };

    private readonly INodeStore _nodes;

    public NodeStoreMediaHandler(INodeStore nodes)
        => _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));

    /// <inheritdoc />
    public override WriteMedia Media => WriteMedia.Sqlite;

    /// <inheritdoc />
    public override IReadOnlyList<string> Collections => OwnedCollections;

    /// <inheritdoc />
    public override Task ApplyAsync(WriteWork work, CancellationToken ct)
    {
        var key = RequireKey(work);
        return work.Collection == WriteCollections.NodesDelete
            ? DeleteAsync(key, ct)
            : PutAsync(key, work.Payload, ct);
    }

    private async Task DeleteAsync(string nodeKey, CancellationToken ct)
        => await _nodes.DeleteAsync(nodeKey, ct).ConfigureAwait(false);

    private async Task PutAsync(string nodeKey, JsonElement payload, CancellationToken ct)
    {
        var isObject = payload.ValueKind == JsonValueKind.Object;
        var record = new NodeRecord
        {
            NodeKey = nodeKey,
            NodeType = isObject ? Str(payload, "nodeType") ?? NodeTypes.Node : NodeTypes.Node,
            ThreadId = isObject ? Str(payload, "threadId") : null,
            Key = isObject ? Str(payload, "key") : null,
            Status = (isObject ? Str(payload, "status") : null) ?? "executed",
            Payload = Plate.DslMethod.Usharp.UsharpValueJson.ParseOrString(isObject ? NodeOutputCodec.EncodeExternal(payload.GetRawText(), nodeKey) : "{}"),
        };
        await _nodes.PutAsync(record, ct).ConfigureAwait(false);
    }

    private static string? Str(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string RequireKey(WriteWork work)
        => string.IsNullOrWhiteSpace(work.Key)
            ? throw new InvalidOperationException($"写步骤 '{work.Id}' 缺主键 Key（{work.Collection} 需要 nodeKey）。")
            : work.Key!;
}

/// <summary>
/// ur_histories 唯一写端口处理器（集合 <see cref="WriteCollections.Histories"/> / <see cref="WriteCollections.HistoriesDelete"/>）。
/// 经既有 <see cref="IHistoryStore"/> 单写者落库；载荷 = <see cref="ConversationLog"/> 全量 JSON（真源形态）。
/// </summary>
public sealed class HistoryStoreMediaHandler : WriteMediaHandlerBase
{
    private static readonly IReadOnlyList<string> OwnedCollections =
        new[] { WriteCollections.Histories, WriteCollections.HistoriesDelete };

    private readonly IHistoryStore _histories;

    public HistoryStoreMediaHandler(IHistoryStore histories)
        => _histories = histories ?? throw new ArgumentNullException(nameof(histories));

    /// <inheritdoc />
    public override WriteMedia Media => WriteMedia.Sqlite;

    /// <inheritdoc />
    public override IReadOnlyList<string> Collections => OwnedCollections;

    /// <inheritdoc />
    public override Task ApplyAsync(WriteWork work, CancellationToken ct)
    {
        var sessionId = string.IsNullOrWhiteSpace(work.Key)
            ? throw new InvalidOperationException($"写步骤 '{work.Id}' 缺主键 Key（{work.Collection} 需要 sessionId）。")
            : work.Key!;
        if (work.Collection == WriteCollections.HistoriesDelete)
            return _histories.DeleteAsync(sessionId, ct);
        var log = ConversationLog.Deserialize(work.Payload.GetRawText());
        return log is null
            ? throw new InvalidOperationException($"写步骤 '{work.Id}' 载荷无法反序列化为 ConversationLog。")
            : _histories.UpsertAsync(sessionId, log, ct);
    }
}

/// <summary>
/// 委托型介质处理器：供组合根桥接「需要泛型/图语义装配」的端口（如 <c>ITaskPoolPersistence&lt;AgentTaskItem&gt;</c>、
/// 引用/归属边写门），使写门本身保持 LLM-free 且不耦合具体泛型/图存储类型（对齐 NodeReadView 的委托注入范式）。
/// </summary>
public sealed class DelegateMediaHandler : WriteMediaHandlerBase
{
    private readonly WriteMedia _media;
    private readonly IReadOnlyList<string> _collections;
    private readonly Func<WriteWork, CancellationToken, Task> _apply;
    private readonly Func<WriteWork, CancellationToken, Task>? _compensate;

    public DelegateMediaHandler(
        WriteMedia media,
        IReadOnlyList<string> collections,
        Func<WriteWork, CancellationToken, Task> apply,
        Func<WriteWork, CancellationToken, Task>? compensate = null)
    {
        _media = media;
        _collections = collections ?? throw new ArgumentNullException(nameof(collections));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _compensate = compensate;
    }

    /// <inheritdoc />
    public override WriteMedia Media => _media;

    /// <inheritdoc />
    public override IReadOnlyList<string> Collections => _collections;

    /// <inheritdoc />
    public override Task ApplyAsync(WriteWork work, CancellationToken ct) => _apply(work, ct);

    /// <inheritdoc />
    public override Task CompensateAsync(WriteWork work, CancellationToken ct)
        => _compensate is null ? Task.CompletedTask : _compensate(work, ct);
}
