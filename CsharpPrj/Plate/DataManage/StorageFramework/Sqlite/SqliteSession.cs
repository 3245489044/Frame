using System;
using System.Collections.Generic;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.Storage;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>Sqlite 命令执行实例（连接串由构造注入，消除全局静态 <c>SqliteRelate.DataBasePath</c>）。</summary>
public sealed class SqliteSession : IDbCommandExecutor
{
    private readonly string _connectionString;

    public SqliteSession(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("连接串不能为空。", nameof(connectionString));
        _connectionString = connectionString;
    }

    public SqliteSession(IDbConnectionFactory connectionFactory)
        : this(connectionFactory?.ConnectionString ?? throw new ArgumentNullException(nameof(connectionFactory)))
    {
    }

    public int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null)
    {
        var rows = 0;
        SqliteRelate.DataUniversal.ExecuteSQL(_connectionString, sql, ToDict(parameters), cmd => rows = cmd.ExecuteNonQuery());
        return rows;
    }

    public long ExecuteInsertReturningId(string sql, IDictionary<string, object>? parameters = null)
    {
        long id = 0;
        SqliteRelate.DataUniversal.ExecuteSQL(_connectionString, sql, ToDict(parameters), cmd =>
        {
            cmd.ExecuteNonQuery();
            id = cmd.Connection.LastInsertRowId;
        });
        return id;
    }

    public object? ExecuteScalar(string sql, IDictionary<string, object>? parameters = null)
        => SqliteRelate.DataUniversal.ExecuteScalar(_connectionString, sql, ToDict(parameters));

    public IReadOnlyList<IReadOnlyDictionary<string, object>> ExecuteQuery(string sql, IDictionary<string, object>? parameters = null)
    {
        var result = new List<IReadOnlyDictionary<string, object>>();
        SqliteRelate.DataUniversal.ExecuteQueryCore(_connectionString, sql, r => result.Add(SqliteRows.ReadRow(r)), readAllRows: true, parameters: ToDict(parameters));
        return result;
    }

    private static Dictionary<string, object>? ToDict(IDictionary<string, object>? parameters)
        => parameters is null ? null : new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
}
