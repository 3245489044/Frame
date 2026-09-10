using System.Collections.Generic;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>命令执行端口。后端适配器把 SQL/参数/结果映射为统一签名（业务无关，基于底层驱动）。参数键沿用 SQLite 约定（含 <c>@</c> 前缀）。</summary>
public interface IDbCommandExecutor
{
    int ExecuteNonQuery(string sql, IDictionary<string, object>? parameters = null);

    /// <summary>执行一条 INSERT 并返回其自增主键（在同一连接/事务上读取 <c>last_insert_rowid</c>，
    /// 避免跨连接调用 <see cref="ExecuteScalar"/> 拿到 0 / 陈旧值）。</summary>
    long ExecuteInsertReturningId(string sql, IDictionary<string, object>? parameters = null);

    object? ExecuteScalar(string sql, IDictionary<string, object>? parameters = null);

    IReadOnlyList<IReadOnlyDictionary<string, object>> ExecuteQuery(string sql, IDictionary<string, object>? parameters = null);
}
