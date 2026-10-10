using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// SQLite 会话历史容器存储（ur_histories 表）—— 以 <see cref="ConversationLog"/> 为统一存储模型。
/// 真源列 <c>[Doc]</c>（文本，经 <see cref="IDocumentCodec"/> 编解码，注入 USharp codec ⇒ USharp 文本）
/// + 派生索引列 <c>[IndexJson]</c>（JSON，供 <c>json_extract</c>）；NodeKey = 会话 ID 全局唯一。
/// 文档 = 含 ThreadId/SessionId/Conversation 的对象（Conversation = ConversationLog 全量序列化，消息列表 + 累计轮次）。
/// ThreadId 固定命名空间 "history"（随文档持久化，不再单独成列）。
/// </summary>
public sealed class SqliteHistoryStore : KvJsonStoreBase, IHistoryStore
{
    /// <summary>历史容器状态根的固定 ThreadId 命名空间（随文档持久化；NodeKey = 会话 ID 全局唯一）。</summary>
    public const string HistoryThreadId = "history";

    private readonly IDocumentCodec _codec;

    /// <summary>注入命令执行器 + 表名（前缀 + <see cref="TableNames.Histories"/>）。
    /// 编解码缺省 = JSON（零行为变更）；注入 <c>UsharpDocumentCodec</c> ⇒ 文档 USharp 化。</summary>
    public SqliteHistoryStore(IDbCommandExecutor executor, string tablePrefix, IDocumentCodec? codec = null)
        : base(executor, tablePrefix, TableNames.Histories)
    {
        _codec = codec ?? JsonDocumentCodec.Instance;
    }

    public string TableName => Table;

    /// <summary>幂等建表：真源列 <c>[Doc]</c> + 派生索引列 <c>[IndexJson]</c>（与 ur_nodes 同构）。
    /// 旧版四列 <see cref="MigrateLegacySchema"/> / 旧 KvJson 三列 <see cref="KvDocumentTable.MigrateFromKvJson"/> 自动收敛。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 旧版 ThreadId/SessionId/SnapshotJson 四列结构先折入 KvJson 三列（整表重建；仅旧库命中，幂等）。
        MigrateLegacySchema();
        // 数据即声明：真源列 [Doc]（USharp 文本）+ 派生索引列 [IndexJson]；旧 KvJson 三列就地迁移。
        KvDocumentTable.EnsureCreated(Executor, Table);
        KvDocumentTable.MigrateFromKvJson(Executor, Table);
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

    /// <summary>幂等写整条会话日志（会话 ID 维度 upsert）；真源 <c>[Doc]</c> 经 codec，派生 <c>[IndexJson]</c> 为同构 JSON。</summary>
    public Task UpsertAsync(string sessionId, ConversationLog log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        ct.ThrowIfCancellationRequested();
        var doc = BuildDoc(sessionId, log.Serialize());
        var text = _codec.Serialize(doc);
        // 派生索引列：仅供 json_extract 索引（真源 = [Doc]）。
        var indexJson = JsonSerializer.Serialize(doc, JsonDocumentCodec.JsonOptions);
        ExecuteNonQuery(KvDocumentTable.UpsertSql(Table), Params(("k", sessionId), ("d", text), ("ix", indexJson)));
        return Task.CompletedTask;
    }

    public Task<ConversationLog?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(KvDocumentTable.SelectByKeySql(Table, KvDocumentTable.ColDoc), Params(("k", sessionId)));
        if (rows.Count == 0) return Task.FromResult<ConversationLog?>(null);
        var doc = _codec.Deserialize<HistoryDoc>(SqliteRows.Str(rows[0], "Doc"));
        return Task.FromResult(doc is null ? null : ConversationLog.Deserialize(doc.Conversation));
    }

    public Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query($"SELECT {KvDocumentTable.ColNodeKey} FROM [{Table}] ORDER BY {KvDocumentTable.ColId}");
        var list = rows
            .Select(r => SqliteRows.Str(r, "NodeKey"))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var removed = ExecuteNonQuery(KvDocumentTable.DeleteByKeySql(Table), Params(("k", sessionId)));
        return Task.FromResult(removed > 0);
    }

    /// <summary>构造历史文档（ThreadId/SessionId/Conversation；Conversation 为消息数组 JSON 文本）。</summary>
    private static HistoryDoc BuildDoc(string sessionId, string conversationJson)
        => new() { ThreadId = HistoryThreadId, SessionId = sessionId, Conversation = conversationJson };

    /// <summary>历史 JSON 文档形态（键名 + 一个 JSON 的载荷部分）。</summary>
    private sealed class HistoryDoc
    {
        public string? ThreadId { get; set; }

        public string? SessionId { get; set; }

        public string? Conversation { get; set; }
    }
}