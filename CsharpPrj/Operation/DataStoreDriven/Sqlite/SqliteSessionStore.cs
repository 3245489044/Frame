using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>会话实例表（ur_sessions）—— session 恰属一个 owner（history/task），角色创建后不变。</summary>
public sealed class SqliteSessionStore : KvJsonStoreBase, ISessionStore
{
    private const string TableId = "Sessions";
    private const string FieldOwnerKind = "OwnerKind";
    private const string FieldOwnerKey = "OwnerKey";
    private const string FieldRole = "Role";
    private const string FieldAgent = "Agent";
    private const string FieldCreatedAt = "CreatedAt";

    private readonly IDocumentCodec _codec;
    private readonly ITableSchemaSource? _schemaSource;

    public SqliteSessionStore(IDbCommandExecutor executor, string tablePrefix, IDocumentCodec codec,
        ITableSchemaSource? schemaSource = null)
        : base(executor, tablePrefix, TableNames.Sessions)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _schemaSource = schemaSource;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var declared = _schemaSource?.GetColumns(TableNames.Sessions) ?? _schemaSource?.GetColumns("sessions");
        KvUsharpTable.EnsureCreated(Executor, Table, declared);
        return Task.CompletedTask;
    }

    public Task<SessionRecord?> GetAsync(SessionKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return Task.FromResult<SessionRecord?>(null);
        var rows = Query(KvUsharpTable.SelectByKeySql(Table), Params(("k", key.Value)));
        return Task.FromResult(rows.Count == 0 ? null : Read(key.Value, SqliteRows.Str(rows[0], "Doc")));
    }

    public Task<IReadOnlyList<SessionRecord>> ListByOwnerAsync(SessionOwner owner, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query($"SELECT {KvUsharpTable.ColNodeKey}, Doc FROM [{Table}]");
        var list = new List<SessionRecord>();
        foreach (var row in rows)
        {
            // 结果集字典键 = 无引号列名（ColNodeKey 带方括号，仅用于 SQL 文本）。
            var key = SqliteRows.Str(row, KvUsharpTable.NodeKeyName);
            var record = string.IsNullOrWhiteSpace(key) ? null : Read(key, SqliteRows.Str(row, "Doc"));
            if (record is null) continue;
            if (record.Owner.Kind == owner.Kind && string.Equals(record.Owner.Key, owner.Key, StringComparison.Ordinal))
                list.Add(record);
        }
        return Task.FromResult<IReadOnlyList<SessionRecord>>(list);
    }

    public Task CreateAsync(SessionRecord session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ct.ThrowIfCancellationRequested();
        var record = new UsharpRecord(new[]
        {
            new KeyValuePair<string, UsharpValue>(FieldOwnerKind, UsharpScalar.Str(session.Owner.Kind.ToString())),
            new KeyValuePair<string, UsharpValue>(FieldOwnerKey, UsharpScalar.Str(session.Owner.Key)),
            new KeyValuePair<string, UsharpValue>(FieldRole, UsharpScalar.Str(session.Role.ToString())),
            new KeyValuePair<string, UsharpValue>(FieldAgent, UsharpScalar.Str(session.Agent)),
            new KeyValuePair<string, UsharpValue>(FieldCreatedAt, UsharpScalar.Str(session.CreatedAt.ToString("O"))),
        });
        var text = _codec.SerializeRecord("session:" + session.Key.Value, TableId, session.Key.Value, record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", session.Key.Value), ("d", text)));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return Task.FromResult(false);
        var removed = ExecuteNonQuery(KvUsharpTable.DeleteByKeySql(Table), Params(("k", key.Value)));
        return Task.FromResult(removed > 0);
    }

    private SessionRecord? Read(string key, string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return null;
        if (!_codec.TryDeserializeRecord(doc, out _, out _, out _, out var body)) return null;
        var ownerKind = Enum.TryParse<SessionOwnerKind>(SqliteHistoryRootStore.ReadString(body, FieldOwnerKind), true, out var ok)
            ? ok : SessionOwnerKind.Task;
        var ownerKey = SqliteHistoryRootStore.ReadString(body, FieldOwnerKey) ?? key;
        var role = Enum.TryParse<SessionRole>(SqliteHistoryRootStore.ReadString(body, FieldRole), true, out var r)
            ? r : SessionRole.TaskMember;
        var agent = SqliteHistoryRootStore.ReadString(body, FieldAgent) ?? "history";
        var createdAt = DateTimeOffset.TryParse(SqliteHistoryRootStore.ReadString(body, FieldCreatedAt), out var at)
            ? at : DateTimeOffset.UtcNow;
        return new SessionRecord(SessionKey.Create(key), new SessionOwner(ownerKind, ownerKey), role, agent, createdAt);
    }
}
