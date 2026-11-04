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
        var existing = await _nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
        // ERR-32：ThreadId 缺省不得落 null（否则按 ThreadId 枚举/信号/作用域读全部看不见该节点）；
        // 依次取 载荷 threadId / workflowId → 既有记录归属 → 默认工作流命名空间。
        var threadId = (isObject ? Str(payload, "threadId") ?? Str(payload, "workflowId") : null)
                       ?? existing?.ThreadId
                       ?? NodeKeys.DefaultWorkflowId;
        // ERR-32：非对象载荷不再压成 "{}"（数组/标量原样透传，由 Codec 判定是否业务形态）。
        var raw = payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : payload.GetRawText();
        var record = new NodeRecord
        {
            NodeKey = nodeKey,
            NodeType = (isObject ? Str(payload, "nodeType") : null) ?? existing?.NodeType ?? NodeTypes.Node,
            ThreadId = threadId,
            Key = (isObject ? Str(payload, "key") : null) ?? existing?.Key,
            Status = (isObject ? Str(payload, "status") : null) ?? existing?.Status ?? "executed",
            Payload = Plate.DslMethod.Usharp.UsharpValueJson.ParseOrString(NodeOutputCodec.EncodeExternal(raw, nodeKey)),
            // ERR-32：写步骤不自带关系列，读-改-写须保留既有 Relations，避免把已建边清空。
            Relations = existing?.Relations,
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
/// 会话历史容器唯一写端口处理器（集合 <see cref="WriteCollections.Histories"/> / <see cref="WriteCollections.HistoriesDelete"/>）。
/// 经既有 <see cref="IConversationLogStore"/> 单写者落库；载荷 = <see cref="ConversationLog"/> 全量 JSON（真源形态）。
/// </summary>
public sealed class ConversationLogMediaHandler : WriteMediaHandlerBase
{
    private static readonly IReadOnlyList<string> OwnedCollections =
        new[] { WriteCollections.Histories, WriteCollections.HistoriesDelete };

    private readonly IConversationLogStore _histories;

    public ConversationLogMediaHandler(IConversationLogStore histories)
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
        => _compensate is null
            ? throw new NotSupportedException(
                $"介质 {_media} 的集合 [{string.Join(", ", _collections)}] 未装配补偿器，写步骤 '{work.Id}' 不可补偿（不得伪装为已补偿）。")
            : _compensate(work, ct);
}
