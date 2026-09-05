using System;
using System.Collections.Generic;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 同构「业务键 NodeKey + 单个 Json」SQLite 存储基类：统一注入环境事务感知执行器 + 表名解析，
/// 提供 Key→Json 的增删改参数化封装（单往返，收敛各 Store 重复的 executor 包装与 SQL 模板）。派生类承载具体 JSON 文档序列化。
/// </summary>
public abstract class KvJsonStoreBase
{
    /// <summary>环境事务感知执行器（已包装 <see cref="AmbientTransactionAwareCommandExecutor"/>，事务内命令自动路由到事务连接）。</summary>
    protected readonly IDbCommandExecutor Executor;

    /// <summary>解析后的表名。</summary>
    protected readonly string Table;

    /// <summary>直接注入完整表名。</summary>
    protected KvJsonStoreBase(IDbCommandExecutor executor, string tableName)
    {
        Executor = new AmbientTransactionAwareCommandExecutor(executor ?? throw new ArgumentNullException(nameof(executor)));
        Table = string.IsNullOrWhiteSpace(tableName)
            ? throw new ArgumentException("表名不能为空。", nameof(tableName))
            : tableName;
    }

    /// <summary>按表前缀 + 固定后缀构造表名。</summary>
    protected KvJsonStoreBase(IDbCommandExecutor executor, string tablePrefix, string suffix)
        : this(executor, PersistenceHelper.ResolveTablePrefix(tablePrefix) + suffix)
    {
    }

    /// <summary>构造参数字典（统一 OrdinalIgnoreCase）。</summary>
    protected static Dictionary<string, object> Params(params (string Key, object Value)[] pairs)
        => PersistenceHelper.CreateParameters(pairs);

    /// <summary>幂等建表（三列表）。</summary>
    protected void EnsureCreated() => KvJsonTable.EnsureCreated(Executor, Table);

    /// <summary>按 NodeKey 查询（返回原始行集）。</summary>
    protected IReadOnlyList<IReadOnlyDictionary<string, object>> Query(string sql, IDictionary<string, object>? parameters = null)
        => Executor.ExecuteQuery(sql, parameters);

    /// <summary>执行写语句，返回受影响行数。</summary>
    protected int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null)
        => Executor.ExecuteNonQuery(sql, parameters);

    /// <summary>执行 INSERT 并返回自增 Id（upsert 冲突更新时返回被更新行 Id）。</summary>
    protected long InsertReturningId(string sql, IDictionary<string, object>? parameters = null)
        => Executor.ExecuteInsertReturningId(sql, parameters);

    /// <summary>执行标量查询（如 COUNT），返回首列首行值。</summary>
    protected object? Scalar(string sql, IDictionary<string, object>? parameters = null)
        => Executor.ExecuteScalar(sql, parameters);
}
