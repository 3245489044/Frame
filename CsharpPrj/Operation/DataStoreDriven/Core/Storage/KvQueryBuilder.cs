using System;
using System.Collections.Generic;
using System.Globalization;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 查询/分页构建单源（U5）：<c>usharp_get</c> 等值条件 + SQL 侧下推分页。
/// <para>统一为 SQL 下推，禁止「一处下推、一处内存截断」。</para>
/// </summary>
public static class KvQueryBuilder
{
    /// <summary>单条 <c>usharp_get([Doc],'$.path')=@param</c> 条件。</summary>
    public static string Eq(string path, string parameter)
        => $"{KvUsharpTable.UsharpExtract(path)}={parameter}";

    /// <summary>以 AND 连接条件；无条件 ⇒ 恒真。</summary>
    public static string And(params string[] clauses)
        => clauses is null || clauses.Length == 0 ? "1=1" : string.Join(" AND ", clauses);

    /// <summary>分页子句（SQL 下推；limit/offset 均已夹紧），返回 <c>" LIMIT n OFFSET m"</c>。</summary>
    public static string Page(int limit, int offset)
    {
        var safeLimit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 5000);
        var safeOffset = Math.Max(0, offset);
        return string.Create(CultureInfo.InvariantCulture, $" LIMIT {safeLimit} OFFSET {safeOffset}");
    }

    /// <summary>有界枚举子句（仅 LIMIT）。</summary>
    public static string Limit(int limit)
    {
        var safeLimit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 5000);
        return string.Create(CultureInfo.InvariantCulture, $" LIMIT {safeLimit}");
    }

    /// <summary>计数 SQL。</summary>
    public static string CountSql(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return $"SELECT COUNT(*) FROM [{table}]";
    }

    /// <summary>参数表（统一 OrdinalIgnoreCase）。</summary>
    public static Dictionary<string, object> Parameters(params (string Key, object Value)[] pairs)
        => PersistenceHelper.CreateParameters(pairs);
}
