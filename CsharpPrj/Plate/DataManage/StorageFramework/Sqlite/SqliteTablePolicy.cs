using System;
using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// 用户表白名单 + 行数封顶 **单源**（只读器与批端口共用，消除两套口径）。
/// <para>
/// 白名单/封顶字面量在本类型内**恰一处**（其余为引用）；门禁：<c>SqliteTablePolicySingleSource</c>。
/// 承载「防注入（仅 sqlite_master 真实业务表）+ 防无界读（LIMIT 封顶）」两条知识的唯一权威。
/// </para>
/// </summary>
public static class SqliteTablePolicy
{
    /// <summary>行数缺省上限（防无界读库）。</summary>
    public const int DefaultLimit = 200;

    /// <summary>行数硬上限（任何请求不得越过）。</summary>
    public const int MaxLimit = 1000;

    /// <summary>表名白名单判定（防注入）：仅允许 sqlite_master 中真实存在的业务表（排除 sqlite_* 系统表）。</summary>
    public static IReadOnlyList<string> ListUserTables(IDbCommandExecutor executor)
        => executor.ExecuteQuery("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .Select(r => r.TryGetValue("name", out var v) ? v?.ToString() : null)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

    /// <summary>表是否存在于用户表白名单。</summary>
    public static bool IsUserTable(IDbCommandExecutor executor, string? table)
        => !string.IsNullOrWhiteSpace(table)
            && ListUserTables(executor).Contains(table!, StringComparer.OrdinalIgnoreCase);

    /// <summary>行数封顶：非正/缺省 → <see cref="DefaultLimit"/>，越过 <see cref="MaxLimit"/> → 收敛到上限。</summary>
    public static int ClampLimit(int? requested)
        => Math.Max(1, Math.Min(requested is > 0 ? requested.Value : DefaultLimit, MaxLimit));
}
