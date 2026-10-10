using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>Sqlite 快照存储（ur_tasks 状态集合，与任务池同表双角色）。按 (ThreadId, SessionId) 幂等覆写最新快照。
/// 真源列 <c>[Doc]</c>（文本，经 <see cref="IDocumentCodec"/> 编解码；注入 USharp codec ⇒ USharp 文本）
/// + 派生索引列 <c>[IndexJson]</c>（JSON，供 <c>json_extract</c>）；NodeKey = 快照键（sessionId，缺省回落 threadId）。</summary>
public sealed class SqliteSnapshotStore : KvJsonStoreBase, ISnapshotStore
{
    private readonly IDocumentCodec _codec;

    public SqliteSnapshotStore(IDbCommandExecutor executor, string table, IDocumentCodec? codec = null)
        : base(executor, table)
    {
        _codec = codec ?? JsonDocumentCodec.Instance;
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        ct.ThrowIfCancellationRequested();
        // NodeKey = 快照键（sessionId，缺省回落 threadId）。
        var key = string.IsNullOrWhiteSpace(sessionId) ? threadId : sessionId;
        // B3 空快照守卫：空/空白原样存空文本（Load 侧按空判 null），不构造文档对象。
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            ExecuteNonQuery(KvDocumentTable.UpsertSql(Table), Params(("k", key), ("d", ""), ("ix", "")));
            return Task.CompletedTask;
        }
        var doc = BuildDoc(snapshotJson, threadId, sessionId);
        var text = _codec.Serialize(doc);
        // 派生索引列：仅供 json_extract 索引（真源 = [Doc]）。
        var indexJson = doc.ToJsonString(JsonDocumentCodec.JsonOptions);
        ExecuteNonQuery(KvDocumentTable.UpsertSql(Table), Params(("k", key), ("d", text), ("ix", indexJson)));
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string sql;
        Dictionary<string, object> p;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            sql = KvDocumentTable.SelectByKeySql(Table, KvDocumentTable.ColDoc);
            p = Params(("k", sessionId));
        }
        else
        {
            sql = $"SELECT {KvDocumentTable.ColDoc} FROM [{Table}] WHERE json_extract({KvDocumentTable.ColIndexJson},'{KvJsonTable.Path.ThreadId}')=@t ORDER BY {KvDocumentTable.ColId} DESC LIMIT 1";
            p = Params(("t", threadId));
        }

        var rows = Query(sql, p);
        if (rows.Count == 0) return Task.FromResult<string?>(null);
        var doc = SqliteRows.Str(rows[0], "Doc");
        if (string.IsNullOrWhiteSpace(doc))
            return Task.FromResult<string?>(null); // B3：表内 NULL 或空快照 → null（「无快照」语义），勿返回空串失真
        // 对外保持 JSON 契约：真源 [Doc] 经 codec 还原为同构 JSON 对象文本（解析失败则原样返回）。
        var node = _codec.Deserialize<JsonObject>(doc);
        return Task.FromResult<string?>(node?.ToJsonString() ?? doc);
    }

    /// <summary>把快照载荷规整为文档对象：对象 ⇒ 注入 ThreadId/SessionId（供无 sessionId 时按 ThreadId 检索）；非对象（异常载荷/裸值）⇒ 包为 Snapshot 字段。</summary>
    private static JsonObject BuildDoc(string snapshotJson, string threadId, string? sessionId)
    {
        JsonNode? node = null;
        try { node = JsonNode.Parse(snapshotJson); } catch (JsonException) { node = null; }
        var obj = node as JsonObject ?? new JsonObject { ["Snapshot"] = node ?? JsonValue.Create(snapshotJson) };
        obj["ThreadId"] = threadId;
        obj["SessionId"] = sessionId ?? string.Empty;
        return obj;
    }
}