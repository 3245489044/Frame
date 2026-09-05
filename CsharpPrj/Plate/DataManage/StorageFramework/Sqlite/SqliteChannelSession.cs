using System;
using System.Collections.Generic;
using System.Data.SQLite;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.Storage;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// 库级单写通道命令执行实例 —— agent.db 专用（架构 I2「每库单一写通道」的落库执行器）。
/// 与 <see cref="SqliteSession"/>（写经 <see cref="SqliteRelate.DataUniversal"/> 每次新连接 autocommit）不同，
/// 本执行器的**写**经注入的 <see cref="ISqliteWriteHost"/>（单写通道 host，模块内实现）借出**该库唯一常驻写连接**单条 autocommit 执行并立即归还，
/// 使 store 层的单条写与 <see cref="SqliteTransaction"/>（事务，亦经同一通道）共用**同一个写连接/同一把写权**，
/// 从机制上消除「单写通道常驻连接 vs DataUniversal 每次新连接」同库双写面自竞争（运行期写锁长持根因）。
/// 读（ExecuteQuery / ExecuteScalar）走 WAL 只读连接（不占写权、不重设 journal_mode），读与写并发无碍。
/// <para>
/// 装配边界：仅对稳定长活的 agent.db 相关 store（ur_tasks / ur_nodes / KeyMapping 等，组合根装配）换用本执行器；
/// 其它库 / 临时库（测试 teardown 需删库文件）保持 <see cref="SqliteSession"/>，不受单写通道常驻连接锁文件影响。
/// </para>
/// </summary>
public sealed class SqliteChannelSession : IDbCommandExecutor
{
    private readonly ISqliteWriteHost _host;
    private readonly string _connectionString;

    /// <summary>以注入的单写通道 host 构造。host 由上层组合根提供（Modules.Threading 版单写实现），
    /// 使本执行器与 SqliteTransaction 共用同一库级单写写权。</summary>
    public SqliteChannelSession(ISqliteWriteHost host, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("连接串不能为空。", nameof(connectionString));
        _host = host;
        _connectionString = connectionString;
    }

    public SqliteChannelSession(ISqliteWriteHost host, IDbConnectionFactory connectionFactory)
        : this(host, connectionFactory?.ConnectionString ?? throw new ArgumentNullException(nameof(connectionFactory)))
    {
    }

    public int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null)
    {
        var dict = ToDict(parameters);
        return _host.RunWrite(_connectionString, conn =>
        {
            using var cmd = new SQLiteCommand(sql, conn);
            AddParams(cmd, dict);
            return cmd.ExecuteNonQuery();
        });
    }

    public long ExecuteInsertReturningId(string sql, IDictionary<string, object>? parameters = null)
    {
        var dict = ToDict(parameters);
        return _host.RunWrite(_connectionString, conn =>
        {
            using var cmd = new SQLiteCommand(sql, conn);
            AddParams(cmd, dict);
            cmd.ExecuteNonQuery();
            return conn.LastInsertRowId;
        });
    }

    public object? ExecuteScalar(string sql, IDictionary<string, object>? parameters = null)
    {
        // 读走 WAL 只读连接（对标 SqliteTransaction.TransactionalSession.ExecuteScalar 语义，避免经写门/重设 journal_mode）。
        using var conn = SqliteRelate.DataUniversal.SetReadConnect(_connectionString);
        using var cmd = new SQLiteCommand(sql, conn);
        AddParams(cmd, ToDict(parameters));
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object>> ExecuteQuery(string sql, IDictionary<string, object>? parameters = null)
    {
        var result = new List<IReadOnlyDictionary<string, object>>();
        SqliteRelate.DataUniversal.ExecuteQueryCore(_connectionString, sql, r => result.Add(SqliteRows.ReadRow(r)), readAllRows: true, parameters: ToDict(parameters));
        return result;
    }

    private static void AddParams(SQLiteCommand cmd, Dictionary<string, object>? parameters)
    {
        if (parameters is null) return;
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
    }

    private static Dictionary<string, object>? ToDict(IDictionary<string, object>? parameters)
        => parameters is null ? null : new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase);
}
