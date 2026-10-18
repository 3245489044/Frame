using System;
using System.Collections.Generic;
using System.Text.Json;
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

/// <summary>
/// 历史根注册表（ur_histories）—— 只存聚合根身份与展示字段，不存 Conversation 日志。
/// 旧行（含 Conversation）由注入的 <see cref="ILegacyHistoryClassifier"/> 兼容读为根；
/// 新行以 <c>history_root:</c> 记录标记，旧 <see cref="SqliteHistoryStore"/> 不得覆盖根行。
/// </summary>
public sealed class SqliteHistoryRootStore : KvJsonStoreBase, IHistoryRootStore
{
    private const string TableId = "Histories";
    private const string RecordPrefix = "history_root:";
    private const string FieldName = "Name";
    private const string FieldKind = "Kind";
    private const string FieldCreatedBy = "CreatedBy";
    private const string FieldCreatedAt = "CreatedAt";

    private readonly IDocumentCodec _codec;
    private readonly ITableSchemaSource? _schemaSource;
    private readonly ILegacyHistoryClassifier? _legacy;

    public SqliteHistoryRootStore(
        IDbCommandExecutor executor, string tablePrefix, IDocumentCodec codec,
        ITableSchemaSource? schemaSource = null, ILegacyHistoryClassifier? legacy = null)
        : base(executor, tablePrefix, TableNames.Histories)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _schemaSource = schemaSource;
        _legacy = legacy;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var declared = _schemaSource?.GetColumns(TableNames.Histories) ?? _schemaSource?.GetColumns("histories");
        KvUsharpTable.EnsureConverged(Executor, Table, TableConvergencePolicy.ResetDropData);
        KvUsharpTable.EnsureCreated(Executor, Table, declared);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryRoot>> ListRootsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query($"SELECT {KvUsharpTable.ColNodeKey}, Doc FROM [{Table}] ORDER BY {KvUsharpTable.ColId}");
        var roots = new List<HistoryRoot>();
        foreach (var row in rows)
        {
            var key = SqliteRows.Str(row, KvUsharpTable.ColNodeKey);
            if (string.IsNullOrWhiteSpace(key)) continue;
            var root = TryReadRoot(key, SqliteRows.Str(row, "Doc"));
            if (root is not null) roots.Add(root);
        }
        return Task.FromResult<IReadOnlyList<HistoryRoot>>(roots);
    }

    public async Task<bool> ExistsAsync(HistoryKey key, CancellationToken ct = default)
        => await GetAsync(key, ct).ConfigureAwait(false) is not null;

    public Task<HistoryRoot?> GetAsync(HistoryKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return Task.FromResult<HistoryRoot?>(null);
        var rows = Query(KvUsharpTable.SelectByKeySql(Table), Params(("k", key.Value)));
        if (rows.Count == 0) return Task.FromResult<HistoryRoot?>(null);
        return Task.FromResult(TryReadRoot(key.Value, SqliteRows.Str(rows[0], "Doc")));
    }

    public Task CreateAsync(HistoryRoot root, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ct.ThrowIfCancellationRequested();
        var record = new UsharpRecord(new[]
        {
            new KeyValuePair<string, UsharpValue>(FieldName, UsharpScalar.Str(root.Name)),
            new KeyValuePair<string, UsharpValue>(FieldKind, UsharpScalar.Str(root.Kind.ToString())),
            new KeyValuePair<string, UsharpValue>(FieldCreatedBy, UsharpScalar.Str(root.CreatedBy)),
            new KeyValuePair<string, UsharpValue>(FieldCreatedAt, UsharpScalar.Str(root.CreatedAt.ToString("O"))),
        });
        var text = _codec.SerializeRecord(RecordPrefix + root.Key.Value, TableId, root.Key.Value, record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", root.Key.Value), ("d", text)));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(HistoryKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return Task.FromResult(false);
        var removed = ExecuteNonQuery(KvUsharpTable.DeleteByKeySql(Table), Params(("k", key.Value)));
        return Task.FromResult(removed > 0);
    }

    private HistoryRoot? TryReadRoot(string key, string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return null;
        if (!_codec.TryDeserializeRecord(doc, out var recordId, out _, out _, out var body)) return null;
        if (!recordId.StartsWith(RecordPrefix, StringComparison.Ordinal))
        {
            return _legacy?.IsLegacyRoot(key) == true
                ? new HistoryRoot(HistoryKey.Create(key), key, HistoryKind.Imported, "migration", DateTimeOffset.UtcNow)
                : null;
        }

        var name = ReadString(body, FieldName) ?? key;
        var kind = Enum.TryParse<HistoryKind>(ReadString(body, FieldKind), true, out var k) ? k : HistoryKind.User;
        var createdBy = ReadString(body, FieldCreatedBy) ?? "system";
        var createdAt = DateTimeOffset.TryParse(ReadString(body, FieldCreatedAt), out var at) ? at : DateTimeOffset.UtcNow;
        return new HistoryRoot(HistoryKey.Create(key), name, kind, createdBy, createdAt);
    }

    internal static string? ReadString(UsharpRecord body, string field)
    {
        if (!body.TryGet(field, out var value)) return null;
        var json = UsharpValueJson.ToJson(value);
        try { return JsonSerializer.Deserialize<string>(json); }
        catch { return json; }
    }
}
