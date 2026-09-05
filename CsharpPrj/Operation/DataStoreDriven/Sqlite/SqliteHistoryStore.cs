using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// SQLite 会话历史容器存储（ur_histories 表）—— 以 <see cref="ConversationLog"/> 为统一存储模型。
/// 表结构：纯「业务键（NodeKey）+ 一个 Json」（与 ur_nodes 同构），NodeKey = 会话 ID 全局唯一；
/// Json = 含 ThreadId/SessionId/Conversation 的对象（Conversation = ConversationLog 全量序列化，消息列表 + 累计轮次）。
/// ThreadId 固定命名空间 "history"（随 Json 持久化，不再单独成列）。
/// </summary>
public sealed class SqliteHistoryStore : KvJsonStoreBase, IHistoryStore
{
    /// <summary>历史容器状态根的固定 ThreadId 命名空间（随 Json 持久化；NodeKey = 会话 ID 全局唯一）。</summary>
    public const string HistoryThreadId = "history";

    public SqliteHistoryStore(IDbCommandExecutor executor, string tablePrefix)
        : base(executor, tablePrefix, TableNames.Histories)
    {
    }

    public string TableName => Table;

    /// <summary>幂等建表：纯「业务键（NodeKey）+ 一个 Json」（与 ur_nodes 同构）。
    /// 旧版 ThreadId/SessionId/SnapshotJson 四列自动迁移收敛（<see cref="MigrateLegacySchema"/>）。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureCreated();
        // 旧版 ThreadId/SessionId/SnapshotJson 四列结构折入 Json 并重建表（仅旧库命中，幂等）。
        MigrateLegacySchema();
        return Task.CompletedTask;
    }

    /// <summary>旧版 ur_histories（ThreadId/SessionId/SnapshotJson 四列，UNIQUE(ThreadId,SessionId)）数据折入 Json 并重建表；仅旧库命中，幂等。
    /// ThreadId/SessionId 参与旧 UNIQUE 约束不可 DROP COLUMN，故整表重建（NodeKey=SessionId，Json 包为 ThreadId/SessionId/Conversation 对象）。</summary>
    private void MigrateLegacySchema()
    {
        var cols = Query($"PRAGMA table_info([{Table}])");
        var names = cols.Select(r => SqliteRows.Str(r, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("ThreadId") && !names.Contains("SnapshotJson"))
            return; // 已是纯键值表（NodeKey+Json），无需迁移。
        KvJsonTable.MigrateLegacyToKvJson(Executor, Table,
            $"SELECT [Id],[SessionId],json_object('ThreadId',[ThreadId],'SessionId',[SessionId],'Conversation',json([SnapshotJson]))");
    }

    /// <summary>幂等写整条会话日志（会话 ID 维度 upsert；Json = 含 Conversation 的对象，Conversation = log 全量序列化）。</summary>
    public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        ct.ThrowIfCancellationRequested();
        var json = SerializeDoc(sessionId, log.Serialize());
        ExecuteNonQuery(KvJsonTable.UpsertSql(Table), Params(("k", sessionId), ("j", json)));
        return Task.CompletedTask;
    }

    public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(KvJsonTable.SelectByKeySql(Table), Params(("k", sessionId)));
        if (rows.Count == 0) return Task.FromResult<ConversationLog?>(null);
        var doc = DeserializeDoc(SqliteRows.Nullable(rows[0], "Json"));
        return Task.FromResult(doc is null ? null : ConversationLog.Deserialize(doc.Conversation));
    }

    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query($"SELECT {KvJsonTable.ColNodeKey} FROM [{Table}] ORDER BY {KvJsonTable.ColId}");
        var list = rows
            .Select(r => SqliteRows.Str(r, "NodeKey"))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var removed = ExecuteNonQuery(KvJsonTable.DeleteByKeySql(Table), Params(("k", sessionId)));
        return Task.FromResult(removed > 0);
    }

    /// <summary>序列化为含 ThreadId/SessionId/Conversation 的对象 JSON（Conversation 为消息数组 JSON 文本）。</summary>
    private static string SerializeDoc(string sessionId, string conversationJson)
        => JsonSerializer.Serialize(new HistoryDoc { ThreadId = HistoryThreadId, SessionId = sessionId, Conversation = conversationJson }, PersistenceHelper.JsonOptions);

    private static HistoryDoc? DeserializeDoc(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<HistoryDoc>(json, PersistenceHelper.JsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>历史 JSON 文档形态（键名 + 一个 JSON 的载荷部分）。</summary>
    private sealed class HistoryDoc
    {
        public string? ThreadId { get; set; }

        public string? SessionId { get; set; }

        public string? Conversation { get; set; }
    }
}