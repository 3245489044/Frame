using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Modules.Node.Identity;
using Plate.DslMethod.Usharp;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using Operation.DataStoreDriven.Core;
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
            // 结果集字典键 = 无引号列名（ColNodeKey 带方括号，仅用于 SQL 文本）。
            var key = SqliteRows.Str(row, KvUsharpTable.NodeKeyName);
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

        // 行键双形态（迁移期）：规范形态 history:{id} 优先，旧行裸键 {id} 兜底。
        // 单形态查找是「列表可见、删除判不存在」的根因（同表混存两种键形态）。
        // 候选形态唯一来源 = 键面投影（NodeEndpointIds.HistoryRowKeys），本处禁再自建候选表。
        foreach (var rowKey in NodeEndpointIds.HistoryRowKeys(key.Value))
        {
            var rows = Query(KvUsharpTable.SelectByKeySql(Table), Params(("k", rowKey)));
            if (rows.Count == 0) continue;
            var root = TryReadRoot(rowKey, SqliteRows.Str(rows[0], "Doc"));
            if (root is not null) return Task.FromResult<HistoryRoot?>(root);
        }

        return Task.FromResult<HistoryRoot?>(null);
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
        var rowKey = RowKey(root.Key);
        if (rowKey.Length == 0)
        {
            throw new ArgumentException("历史根身份为空，拒绝写入根行。", nameof(root));
        }

        var text = _codec.SerializeRecord(RecordPrefix + rowKey, TableId, rowKey, record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", rowKey), ("d", text)));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(HistoryKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (key.IsEmpty) return Task.FromResult(false);

        // 删除必须命中实际存在的行键形态（规范 / 旧裸键），否则「视为已删除」会留下僵尸行。
        var removed = 0;
        foreach (var rowKey in NodeEndpointIds.HistoryRowKeys(key.Value))
            removed += ExecuteNonQuery(KvUsharpTable.DeleteByKeySql(Table), Params(("k", rowKey)));

        return Task.FromResult(removed > 0);
    }

    /// <summary>身份 → 行键（<c>history:{identity}</c>）；空身份 ⇒ 空串（不伪造行键）。</summary>
    private static string RowKey(HistoryKey key) => NodeEndpointIds.ToHistoryRowKey(key.Value);

    private HistoryRoot? TryReadRoot(string key, string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return null;
        if (!_codec.TryDeserializeRecord(doc, out var recordId, out _, out _, out var body)) return null;
        // D-01=A（Identity 规范面）：HistoryKey 一律裸身份；行键 history:{id} 只在存储边界出现。
        var identity = NodeEndpointIds.NormalizeHistoryId(key);
        if (identity.Length == 0) return null;
        var historyKey = HistoryKey.Create(identity);
        if (!recordId.StartsWith(RecordPrefix, StringComparison.Ordinal))
        {
            return _legacy?.IsLegacyRoot(key) == true
                ? new HistoryRoot(historyKey, identity, HistoryKind.Imported, "migration", DateTimeOffset.MinValue)
                : null;
        }

        var name = ReadString(body, FieldName);
        var kind = Enum.TryParse<HistoryKind>(ReadString(body, FieldKind), true, out var k) ? k : HistoryKind.User;
        var createdBy = ReadString(body, FieldCreatedBy) ?? "system";
        var createdAt = DateTimeOffset.TryParse(ReadString(body, FieldCreatedAt), out var at) ? at : DateTimeOffset.MinValue;
        return new HistoryRoot(historyKey, string.IsNullOrWhiteSpace(name) ? identity : name, kind, createdBy, createdAt);
    }

    /// <summary>字段读取单源（U4）：委托 <see cref="KvPayloadReader.ReadString"/>。</summary>
    internal static string? ReadString(UsharpRecord body, string field)
        => KvPayloadReader.ReadString(body, field);
}
