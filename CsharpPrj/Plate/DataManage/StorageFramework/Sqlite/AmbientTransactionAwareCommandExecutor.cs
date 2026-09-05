using System;
using System.Collections.Generic;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// 环境事务感知命令执行器：包装默认执行器，若当前异步流程内存在环境事务
/// （<see cref="SqliteAmbientTransaction"/>），所有命令自动路由到事务连接；否则回落默认执行器。
/// 消除各 Sqlite Store 重复手写 <c>SqliteAmbientTransaction.Resolve</c> 的横切逻辑（SingleImplementation）。
/// </summary>
public sealed class AmbientTransactionAwareCommandExecutor : IDbCommandExecutor
{
    private readonly IDbCommandExecutor _default;

    public AmbientTransactionAwareCommandExecutor(IDbCommandExecutor defaultExecutor)
        => _default = defaultExecutor ?? throw new ArgumentNullException(nameof(defaultExecutor));

    public int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null)
        => Resolve().ExecuteNonQuery(sql, parameters);

    public long ExecuteInsertReturningId(string sql, IDictionary<string, object>? parameters = null)
        => Resolve().ExecuteInsertReturningId(sql, parameters);

    public object? ExecuteScalar(string sql, IDictionary<string, object>? parameters = null)
        => Resolve().ExecuteScalar(sql, parameters);

    public IReadOnlyList<IReadOnlyDictionary<string, object>> ExecuteQuery(string sql, IDictionary<string, object>? parameters = null)
        => Resolve().ExecuteQuery(sql, parameters);

    /// <summary>当前环境事务存在时路由到事务命令执行器；否则用默认执行器。</summary>
    private IDbCommandExecutor Resolve() => SqliteAmbientTransaction.Resolve(_default);
}
