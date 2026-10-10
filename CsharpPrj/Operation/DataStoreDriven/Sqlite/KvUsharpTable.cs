using System;
using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 纯 USharp 真源表模板：「业务键 NodeKey + USharp 真源文档 Doc」**三列**（<c>[Id]/[NodeKey]/[Doc]</c>）。
/// <para>
/// 与旧 <see cref="KvDocumentTable"/> 的区别：**无兼容派生列 <c>[IndexJson]</c>**。真源 <c>[Doc]</c> = USharp 文本；
/// 过滤不再下推 SQL <c>json_extract</c>（无法作用于 USharp 文本），改由存储在内存解码 USharp 后完成。
/// </para>
/// </summary>
public static class KvUsharpTable
{
    public const string ColId = "[Id]";
    public const string ColNodeKey = "[NodeKey]";
    public const string ColDoc = "[Doc]";

    /// <summary>纯三列结构（真源 Doc；无任何 JSON 影子/派生列）。</summary>
    public static readonly string[] SchemaColumns =
    {
        "[Id] INTEGER PRIMARY KEY AUTOINCREMENT",
        "[NodeKey] TEXT NOT NULL UNIQUE",
        "[Doc] TEXT NOT NULL",
    };

    /// <summary>建表 SQL：<paramref name="columns"/> 缺省（null/空）⇒ 回落手写 <see cref="SchemaColumns"/>。</summary>
    public static string CreateSql(string table, IReadOnlyList<string>? columns = null)
        => PersistenceHelper.CreateTableSql(table, columns is { Count: > 0 } ? columns.ToArray() : SchemaColumns);

    public static void EnsureCreated(IDbCommandExecutor executor, string table, IReadOnlyList<string>? columns = null)
        => executor.ExecuteNonQuery(CreateSql(table, columns));

    /// <summary>
    /// 收敛为纯三列（退役兼容派生列 <c>[IndexJson]</c> 与旧 <c>[Json]</c>）：整表重建为
    /// <c>[Id]/[NodeKey]/[Doc]</c>，Doc 取既有 <c>[Doc]</c>（优先）或旧 <c>[Json]</c>。表结构已是纯三列 ⇒ 跳过（幂等）。
    /// </summary>
    public static void MigrateToPure(IDbCommandExecutor executor, string table)
    {
        var cols = executor.ExecuteQuery($"PRAGMA table_info([{table}])")
            .Select(r => (r.TryGetValue("name", out var v) ? v?.ToString() : null) ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cols.Count == 0) return;                                        // 表不存在（EnsureCreated 先建）
        if (cols.Contains("Doc") && !cols.Contains("IndexJson") && !cols.Contains("Json"))
            return;                                                         // 已是纯三列

        var docExpr = cols.Contains("Doc") ? "[Doc]" : (cols.Contains("Json") ? "[Json]" : "''");
        var tmp = table + "_pure";
        Exec($"DROP TABLE IF EXISTS [{tmp}];");
        Exec($"CREATE TABLE [{tmp}] ({ColId} INTEGER PRIMARY KEY AUTOINCREMENT, {ColNodeKey} TEXT NOT NULL UNIQUE, {ColDoc} TEXT NOT NULL);");
        Exec($"INSERT INTO [{tmp}] ({ColId},{ColNodeKey},{ColDoc}) SELECT {ColId},{ColNodeKey},COALESCE({docExpr},'') FROM [{table}];");
        Exec($"DROP TABLE [{table}];");
        Exec($"ALTER TABLE [{tmp}] RENAME TO [{table}];");

        void Exec(string sql)
        {
            try { executor.ExecuteNonQuery(sql); }
            catch (Exception ex) { throw new InvalidOperationException($"纯 USharp 迁移失败: {sql}", ex); }
        }
    }

    /// <summary>
    /// 若表已存在且**非**纯三列 ⇒ 丢弃重建（数据口径 = 无视删除：不迁移旧行）。返回是否发生重置。
    /// 供任务池/快照/历史在初始化时收敛表结构（旧四列 [IndexJson] / 旧 KvJson [Json] / 更老多列一律作废）。
    /// </summary>
    public static bool ResetToPureIfNeeded(IDbCommandExecutor executor, string table)
    {
        var cols = executor.ExecuteQuery($"PRAGMA table_info([{table}])")
            .Select(r => (r.TryGetValue("name", out var v) ? v?.ToString() : null) ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cols.Count == 0) return false;                                   // 表不存在（EnsureCreated 先建）
        if (cols.SetEquals(new[] { "Id", "NodeKey", "Doc" })) return false;  // 已是纯三列
        executor.ExecuteNonQuery($"DROP TABLE [{table}];");
        return true;
    }

    /// <summary>按 NodeKey 查询的列投影前缀（含 <c>LIMIT 1</c>）。</summary>
    public static string SelectByKeySql(string table, string columns = ColDoc)
        => $"SELECT {columns} FROM [{table}] WHERE {ColNodeKey}=@k LIMIT 1";

    public static string DeleteByKeySql(string table)
        => $"DELETE FROM [{table}] WHERE {ColNodeKey}=@k";

    /// <summary>NodeKey+Doc upsert（幂等覆写；onConflictUpdate 缺省整行覆写）。</summary>
    public static string UpsertSql(string table, string onConflictUpdate = "[Doc]=@d")
        => $"INSERT INTO [{table}] ({ColNodeKey},{ColDoc}) VALUES (@k,@d) " +
           $"ON CONFLICT({ColNodeKey}) DO UPDATE SET {onConflictUpdate}";

    /// <summary>
    /// <c>usharp_get([Doc],'$.x')</c> 表达式（集中式转换层，§1.6）：过滤/排序/CAS 于 SQL 侧下推。
    /// 与 <see cref="EnsureUsharpIndex"/> 建索引表达式须**逐字一致**（否则下推不命中）。
    /// </summary>
    public static string UsharpExtract(string path) => $"usharp_get({ColDoc},'{path}')";

    /// <summary>
    /// 建 <c>usharp_get</c> 表达式索引（过滤/排序键下推；每连接须已注册该函数，见 UsharpSqlFunction）。
    /// <paramref name="extra"/> 为附加排序列（如 <c>[Id]</c>）。
    /// </summary>
    public static void EnsureUsharpIndex(IDbCommandExecutor executor, string table, string indexName, string path, string? extra = null)
    {
        var suffix = extra is null ? "" : "," + extra;
        executor.ExecuteNonQuery(
            $"CREATE INDEX IF NOT EXISTS [{indexName}] ON [{table}]({UsharpExtract(path)}{suffix});");
    }
}