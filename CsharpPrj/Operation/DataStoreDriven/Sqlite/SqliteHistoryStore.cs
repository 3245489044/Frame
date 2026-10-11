using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// SQLite 会话历史容器存储（ur_histories 表）—— 以 <see cref="ConversationLog"/> 为统一存储模型。
/// <para>
/// 表为**纯 USharp 三列**；真源 <c>[Doc]</c> = <c>[Record] history:&lt;SessionId&gt;: table=Histories | key=&lt;SessionId&gt;</c> 实例规范文本
/// （ThreadId/SessionId/Conversation 记录字段）；无 <c>[IndexJson]</c>。NodeKey = 会话 ID 全局唯一。
/// ThreadId 固定命名空间 "history"（随文档持久化）。
/// </para>
/// </summary>
public sealed class SqliteHistoryStore : KvJsonStoreBase, IHistoryStore
{
    /// <summary>历史容器状态根的固定 ThreadId 命名空间（随文档持久化；NodeKey = 会话 ID 全局唯一）。</summary>
    public const string HistoryThreadId = "history";

    private const string TableId = "Histories";
    private const string FieldThreadId = "ThreadId";
    private const string FieldSessionId = "SessionId";
    private const string FieldConversation = "Conversation";

    private readonly IDocumentCodec _codec;
    private readonly ITableSchemaSource? _schemaSource;

    /// <summary>注入命令执行器 + 表名前缀。编解码缺省 = JSON；注入 USharp codec ⇒ 文档 USharp 化。
    /// <paramref name="schemaSource"/> 缺省 ⇒ 建表回落手写 <see cref="KvUsharpTable.SchemaColumns"/>（声明面缺件时零行为变更）。</summary>
    public SqliteHistoryStore(IDbCommandExecutor executor, string tablePrefix, IDocumentCodec? codec = null, ITableSchemaSource? schemaSource = null)
        : base(executor, tablePrefix, TableNames.Histories)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec), "ur_histories 记录型存储须注入 IDocumentCodec（USharp codec）。");
        _schemaSource = schemaSource;
    }

    public string TableName => Table;

    /// <summary>幂等建表：**纯三列** <c>[Id]/[NodeKey]/[Doc]</c>（与 ur_nodes 同构）。
    /// 旧四列（<c>[IndexJson]</c>）/ 旧 KvJson 三列 / 更老多列按无视删除口径丢弃重建。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var declared = _schemaSource?.GetColumns(TableNames.Histories) ?? _schemaSource?.GetColumns("histories");
        KvUsharpTable.ResetToPureIfNeeded(Executor, Table);
        KvUsharpTable.EnsureCreated(Executor, Table, declared);
        return Task.CompletedTask;
    }

    /// <summary>幂等写整条会话日志（会话 ID 维度 upsert）；真源 [Doc] = [Record] 实例规范文本。</summary>
    public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        ct.ThrowIfCancellationRequested();
        var record = new UsharpRecord(new[]
        {
            new KeyValuePair<string, UsharpValue>(FieldThreadId, UsharpScalar.Str(HistoryThreadId)),
            new KeyValuePair<string, UsharpValue>(FieldSessionId, UsharpScalar.Str(sessionId)),
            new KeyValuePair<string, UsharpValue>(FieldConversation, ParsePayload(log.Serialize())),
        });
        var text = _codec.SerializeRecord(RecordId(sessionId), TableId, sessionId, record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", sessionId), ("d", text)));
        return Task.CompletedTask;
    }

    public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(KvUsharpTable.SelectByKeySql(Table), Params(("k", sessionId)));
        if (rows.Count == 0) return Task.FromResult<ConversationLog?>(null);
        var doc = SqliteRows.Str(rows[0], "Doc");
        if (string.IsNullOrWhiteSpace(doc)) return Task.FromResult<ConversationLog?>(null);
        if (!_codec.TryDeserializeRecord(doc, out _, out _, out _, out var body)) return Task.FromResult<ConversationLog?>(null);
        if (!body.TryGet(FieldConversation, out var v)) return Task.FromResult<ConversationLog?>(null);
        return Task.FromResult<ConversationLog?>(ConversationLog.Deserialize(UsharpValueJson.ToJson(v)));
    }

    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
        => ListHistoryIdsAsync(0, ct);

    /// <summary>有界枚举（E5）：limit ≤ 0 ⇒ 全量；否则下推 SQL LIMIT（避免全表无界枚举）。</summary>
    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bound = limit > 0 ? $" LIMIT {limit}" : string.Empty;
        var rows = Query($"SELECT {KvUsharpTable.ColNodeKey} FROM [{Table}] ORDER BY {KvUsharpTable.ColId}{bound}");
        var list = rows
            .Select(r => SqliteRows.Str(r, "NodeKey"))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var removed = ExecuteNonQuery(KvUsharpTable.DeleteByKeySql(Table), Params(("k", sessionId)));
        return Task.FromResult(removed > 0);
    }

    private static string RecordId(string sessionId) => "history:" + sessionId;

    /// <summary>会话日志 JSON 文本 → 中立值（非法 JSON ⇒ 不透明字符串标量，原样保留）。</summary>
    private static UsharpValue ParsePayload(string json)
    {
        try { return UsharpValueJson.Parse(json); }
        catch { return UsharpScalar.Str(json); }
    }
}
