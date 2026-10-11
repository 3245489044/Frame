using System;
using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Batch;

/// <summary>
/// <see cref="IBatchStore"/> 的 **SQLite 默认实现**（引擎级、零领域）。
/// <para>
/// 批读沿用 <see cref="Sqlite.SqliteTableReader"/> 语义（用户表白名单校验 + LIMIT 封顶 + 只读）；
/// 批写 / 批删经注入的 <see cref="IDbCommandExecutor"/> 执行 —— 装配期注入
/// <see cref="Sqlite.SqliteChannelSession"/>（库级单写通道）即得「原子 + 单写者」纪律，
/// 亦可传入 <see cref="Sqlite.SqliteTransaction"/> 的会话以取得 worker 事务原子性。
/// </para>
/// </summary>
public sealed class SqliteBatchStore : IBatchStore
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 1000;

    private readonly IDbCommandExecutor _executor;

    /// <param name="executor">命令执行器（SqliteSession / SqliteChannelSession / 事务会话）。</param>
    public SqliteBatchStore(IDbCommandExecutor executor)
        => _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    /// <inheritdoc />
    public BatchReadResult Read(BatchReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Table))
        {
            return new BatchReadResult(Array.Empty<IReadOnlyDictionary<string, object>>(), "（空表名）");
        }

        if (!IsUserTable(request.Table, out var tableError))
        {
            return new BatchReadResult(Array.Empty<IReadOnlyDictionary<string, object>>(), tableError);
        }

        var cap = Math.Max(1, Math.Min(request.Limit <= 0 ? DefaultLimit : request.Limit, MaxLimit));
        string sql;
        IDictionary<string, object>? parameters = null;
        if (request.Keys is { Count: > 0 })
        {
            var names = request.Keys.Select((_, i) => "@k" + i).ToArray();
            sql = $"SELECT * FROM [{request.Table}] WHERE [{request.KeyColumn}] IN ({string.Join(", ", names)}) LIMIT {cap}";
            parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < request.Keys.Count; i++)
            {
                parameters["@k" + i] = request.Keys[i];
            }
        }
        else
        {
            sql = $"SELECT * FROM [{request.Table}] LIMIT {cap}";
        }

        try
        {
            return new BatchReadResult(_executor.ExecuteQuery(sql, parameters));
        }
        catch (Exception ex)
        {
            return new BatchReadResult(Array.Empty<IReadOnlyDictionary<string, object>>(), $"（读取失败）{ex.Message}");
        }
    }

    /// <inheritdoc />
    public BatchResult Upsert(BatchWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Rows is null || request.Rows.Count == 0)
        {
            return BatchResult.Empty;
        }

        var items = new List<BatchItemOutcome>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Key))
            {
                items.Add(BatchItemOutcome.Fail(row?.Key ?? string.Empty, BatchItemStatus.Rejected, "空键"));
                continue;
            }

            try
            {
                var affected = _executor.ExecuteNonQuery(
                    $"INSERT INTO [{request.Table}] ([{request.KeyColumn}], [{request.DocColumn}]) VALUES (@k, @d) " +
                    $"ON CONFLICT([{request.KeyColumn}]) DO UPDATE SET [{request.DocColumn}] = excluded.[{request.DocColumn}]",
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["@k"] = row.Key,
                        ["@d"] = row.Doc ?? string.Empty,
                    });

                items.Add(affected > 0
                    ? BatchItemOutcome.Ok(row.Key)
                    : BatchItemOutcome.Fail(row.Key, BatchItemStatus.Skipped, "无变更"));
            }
            catch (Exception ex)
            {
                items.Add(BatchItemOutcome.Fail(row.Key, BatchItemStatus.Rejected, ex.Message));
            }
        }

        return new BatchResult(items);
    }

    /// <inheritdoc />
    public BatchResult Delete(BatchDeleteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Keys is null || request.Keys.Count == 0)
        {
            return BatchResult.Empty;
        }

        var items = new List<BatchItemOutcome>(request.Keys.Count);
        foreach (var key in request.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                items.Add(BatchItemOutcome.Fail(key ?? string.Empty, BatchItemStatus.Rejected, "空键"));
                continue;
            }

            try
            {
                var affected = _executor.ExecuteNonQuery(
                    $"DELETE FROM [{request.Table}] WHERE [{request.KeyColumn}] = @k",
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["@k"] = key });

                items.Add(affected > 0
                    ? BatchItemOutcome.Ok(key)
                    : BatchItemOutcome.Fail(key, BatchItemStatus.NotFound, null));
            }
            catch (Exception ex)
            {
                items.Add(BatchItemOutcome.Fail(key, BatchItemStatus.Rejected, ex.Message));
            }
        }

        return new BatchResult(items);
    }

    /// <summary>用户表白名单判定（防注入）：仅允许 sqlite_master 中真实存在的业务表。</summary>
    private bool IsUserTable(string table, out string error)
    {
        try
        {
            var rows = _executor.ExecuteQuery(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
            if (rows.Any(r => r.TryGetValue("name", out var v)
                    && string.Equals(v?.ToString(), table, StringComparison.OrdinalIgnoreCase)))
            {
                error = string.Empty;
                return true;
            }

            error = $"（表不存在或非法：{table}）";
            return false;
        }
        catch (Exception ex)
        {
            error = $"（表校验失败）{ex.Message}";
            return false;
        }
    }
}
