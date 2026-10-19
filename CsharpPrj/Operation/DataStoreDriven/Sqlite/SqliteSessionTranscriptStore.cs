using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using Share.Conversation;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 会话日志表（ur_session_logs）—— 对话内容投影；只按 sessionKey 读写，不再触碰 ur_histories。
/// 迁移期旧表回退由 <see cref="LegacyFallbackSessionTranscriptStore"/> 装饰器**单点**承担。
/// </summary>
public sealed class SqliteSessionTranscriptStore : KvJsonStoreBase, ISessionTranscriptStore
{
    private const string TableId = "SessionLogs";
    private const string FieldConversation = "Conversation";
    private const string FieldWatermark = "Watermark";

    private readonly IDocumentCodec _codec;
    private readonly ITableSchemaSource? _schemaSource;

    public SqliteSessionTranscriptStore(IDbCommandExecutor executor, string tablePrefix, IDocumentCodec codec,
        ITableSchemaSource? schemaSource = null)
        : base(executor, tablePrefix, TableNames.SessionLogs)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _schemaSource = schemaSource;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var declared = _schemaSource?.GetColumns(TableNames.SessionLogs) ?? _schemaSource?.GetColumns("session_logs");
        KvUsharpTable.EnsureCreated(Executor, Table, declared);
        return Task.CompletedTask;
    }

    public async Task<ConversationLog?> GetAsync(SessionKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return null;
        var rows = Query(KvUsharpTable.SelectByKeySql(Table), Params(("k", key.Value)));
        if (rows.Count > 0)
        {
            var doc = SqliteRows.Str(rows[0], "Doc");
            if (!string.IsNullOrWhiteSpace(doc)
                && _codec.TryDeserializeRecord(doc, out _, out _, out _, out var body)
                && body.TryGet(FieldConversation, out var value))
            {
                return ConversationLog.Deserialize(UsharpValueJson.ToJson(value));
            }
        }

        return null;
    }

    public async Task AppendProjectionAsync(SessionKey key, ConversationLog log, long watermark, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) throw new ArgumentException("sessionKey 不能为空", nameof(key));

        var existing = await GetAsync(key, ct).ConfigureAwait(false);
        var merged = Merge(existing, log);
        var record = new UsharpRecord(new[]
        {
            new KeyValuePair<string, UsharpValue>(FieldConversation, ParsePayload(merged.Serialize())),
            new KeyValuePair<string, UsharpValue>(FieldWatermark, UsharpScalar.Str(watermark.ToString())),
        });
        var text = _codec.SerializeRecord("session_log:" + key.Value, TableId, key.Value, record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", key.Value), ("d", text)));
    }

    public Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return Task.FromResult(false);
        var removed = ExecuteNonQuery(KvUsharpTable.DeleteByKeySql(Table), Params(("k", key.Value)));
        return Task.FromResult(removed > 0);
    }

    private static ConversationLog Merge(ConversationLog? existing, ConversationLog incoming)
    {
        if (existing is null || existing.Messages.Count == 0) return incoming;
        if (incoming.Messages.Count >= existing.Messages.Count) return incoming;
        var merged = new ConversationLog();
        foreach (var message in existing.Messages) merged.AppendRaw(message);
        return merged;
    }

    private static UsharpValue ParsePayload(string json)
    {
        try { return UsharpValueJson.Parse(json); }
        catch { return UsharpScalar.Str(json); }
    }
}
