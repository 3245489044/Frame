using System;
using System.Collections.Generic;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Batch;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// <see cref="IStoreRowPort"/> 的 **SQLite 实现**（同构三列 <c>[Id]/[NodeKey]/[Doc]</c>）：
/// 组合 <see cref="KvUsharpTable"/>（SQL 单源）+ <see cref="IBatchStore"/>（批量唯一构建点）+ 可选域校验/写后动作委托。
/// <para>
/// ur_nodes / ur_tasks / ur_histories / 快照共用；差异仅在 表名 + 键列名 + 域校验器与写后动作（由域侧注入）。
/// 注入库级单写通道（<see cref="IDbCommandExecutor"/> = SqliteChannelSession / 事务会话）即得「原子 + 单写者」。
/// </para>
/// </summary>
public sealed class SqliteStoreRowPort : IStoreRowPort
{
    private readonly IBatchStore _batch;
    private readonly string _keyColumnBare;
    private readonly string _docColumnBare;
    private readonly Func<string, string, BatchItemOutcome?>? _validate;
    private readonly Func<string, BatchItemOutcome, BatchItemOutcome>? _afterWrite;

    /// <param name="executor">命令执行器（库级单写通道 / 事务会话）。</param>
    /// <param name="place">物理表名（如 <c>ur_nodes</c>）。</param>
    /// <param name="keyColumn">键列名（**不含方括号**，缺省 <c>NodeKey</c>）。</param>
    /// <param name="docColumn">文档列名（**不含方括号**，缺省 <c>Doc</c>）。</param>
    /// <param name="validate">域校验器（返回非 null ⇒ 拒绝并可见化；null ⇒ 放行）。</param>
    /// <param name="afterWrite">写后动作（级联 / 运行态发布；由域声明注入）。</param>
    public SqliteStoreRowPort(
        IDbCommandExecutor executor,
        string place,
        string keyColumn = "NodeKey",
        string docColumn = "Doc",
        Func<string, string, BatchItemOutcome?>? validate = null,
        Func<string, BatchItemOutcome, BatchItemOutcome>? afterWrite = null)
    {
        _batch = new SqliteBatchStore(executor ?? throw new ArgumentNullException(nameof(executor)));
        Place = string.IsNullOrWhiteSpace(place)
            ? throw new ArgumentException("物理落点不能为空。", nameof(place))
            : place;
        _keyColumnBare = Bare(keyColumn, "NodeKey");
        _docColumnBare = Bare(docColumn, "Doc");
        _validate = validate;
        _afterWrite = afterWrite;
    }

    /// <inheritdoc />
    public string Place { get; }

    /// <inheritdoc />
    public string KeyColumn => "[" + _keyColumnBare + "]";

    /// <inheritdoc />
    public bool TryRead(string key, out string docText)
    {
        docText = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var result = _batch.Read(new BatchReadRequest(Place, new[] { key }, _keyColumnBare, Limit: 1));
        var row = result.Rows.FirstOrDefault(r => r.TryGetValue(_keyColumnBare, out var k)
            && string.Equals(k?.ToString(), key, StringComparison.Ordinal)) ?? result.Rows.FirstOrDefault();
        if (row is null) return false;

        if (!TryGet(row, _docColumnBare, out var doc) || doc is null) return false;
        docText = doc.ToString() ?? string.Empty;
        return docText.Length > 0;
    }

    /// <inheritdoc />
    public BatchItemOutcome Upsert(string key, string docText)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BatchItemOutcome.Fail(key ?? string.Empty, BatchItemStatus.Rejected, "空键");
        }

        if (_validate is not null && _validate(key, docText ?? string.Empty) is { } rejection)
        {
            return rejection;
        }

        var result = _batch.Upsert(new BatchWriteRequest(
            Place, new[] { new BatchWriteRow(key, docText ?? string.Empty) }, _keyColumnBare, _docColumnBare));
        var outcome = result.Items.FirstOrDefault() ?? BatchItemOutcome.Fail(key, BatchItemStatus.Rejected, "未执行");
        return _afterWrite is null ? outcome : _afterWrite(key, outcome);
    }

    /// <inheritdoc />
    public BatchItemOutcome Delete(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BatchItemOutcome.Fail(key ?? string.Empty, BatchItemStatus.Rejected, "空键");
        }

        var result = _batch.Delete(new BatchDeleteRequest(Place, new[] { key }, _keyColumnBare));
        var outcome = result.Items.FirstOrDefault() ?? BatchItemOutcome.Fail(key, BatchItemStatus.NotFound, null);
        return _afterWrite is null ? outcome : _afterWrite(key, outcome);
    }

    private static string Bare(string column, string fallback)
    {
        var c = string.IsNullOrWhiteSpace(column) ? fallback : column.Trim();
        return c.StartsWith('[') && c.EndsWith(']') ? c[1..^1] : c;
    }

    private static bool TryGet(IReadOnlyDictionary<string, object> row, string column, out object? value)
    {
        if (row.TryGetValue(column, out value)) return true;
        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }
        value = null;
        return false;
    }
}
