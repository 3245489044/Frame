using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 「业务键 NodeKey + USharp 真源文档 Doc + 派生索引列 IndexJson」表模板。
/// <para>
/// 与 <see cref="KvJsonTable"/> 并存：本模板为**数据即声明**口径（口径 B）——
/// 真源列 <c>[Doc]</c> 存 USharp 文本（编解码经 <c>IDocumentCodec</c>）；
/// <c>[IndexJson]</c> 为**派生**列（写门由 codec 生成），仅供 <c>json_extract</c> 表达式索引。
/// </para>
/// </summary>
public static class KvDocumentTable
{
    public const string ColId = "[Id]";
    public const string ColNodeKey = "[NodeKey]";
    public const string ColDoc = "[Doc]";
    public const string ColIndexJson = "[IndexJson]";

    /// <summary>统一四列结构（真源 Doc + 派生 IndexJson）。</summary>
    public static readonly string[] SchemaColumns =
    {
        "[Id] INTEGER PRIMARY KEY AUTOINCREMENT",
        "[NodeKey] TEXT NOT NULL UNIQUE",
        "[Doc] TEXT NOT NULL",
        "[IndexJson] TEXT",
    };

    public static string CreateSql(string table) => PersistenceHelper.CreateTableSql(table, SchemaColumns);

    public static void EnsureCreated(IDbCommandExecutor executor, string table)
        => executor.ExecuteNonQuery(CreateSql(table));

    /// <summary>旧纯键值表（<c>[Id]/[NodeKey]/[Json]</c>）就地迁移：加 <c>[Doc]</c>/<c>[IndexJson]</c> 列，Json 折入 Doc。</summary>
    public static void MigrateFromKvJson(IDbCommandExecutor executor, string table)
    {
        var cols = executor.ExecuteQuery($"PRAGMA table_info([{table}])")
            .Select(r => (r.TryGetValue("name", out var v) ? v?.ToString() : null) ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cols.Contains("Doc")) return;                       // 已是新表

        if (cols.Contains("Json"))
        {
            executor.ExecuteNonQuery($"ALTER TABLE [{table}] ADD COLUMN [Doc] TEXT NOT NULL DEFAULT '';");
            executor.ExecuteNonQuery($"UPDATE [{table}] SET [Doc]=[Json] WHERE [Doc]='';");
        }
        executor.ExecuteNonQuery($"ALTER TABLE [{table}] ADD COLUMN [IndexJson] TEXT;");
    }

    /// <summary>按 NodeKey 查询的列投影前缀（含 <c>LIMIT 1</c>）。</summary>
    public static string SelectByKeySql(string table, string columns = ColDoc)
        => $"SELECT {columns} FROM [{table}] WHERE {ColNodeKey}=@k LIMIT 1";

    public static string DeleteByKeySql(string table)
        => $"DELETE FROM [{table}] WHERE {ColNodeKey}=@k";

    /// <summary>NodeKey+Doc+IndexJson upsert（幂等覆写；onConflictUpdate 缺省整行覆写）。</summary>
    public static string UpsertSql(string table, string onConflictUpdate = "[Doc]=@d,[IndexJson]=@ix")
        => $"INSERT INTO [{table}] ({ColNodeKey},{ColDoc},{ColIndexJson}) VALUES (@k,@d,@ix) " +
           $"ON CONFLICT({ColNodeKey}) DO UPDATE SET {onConflictUpdate}";

    /// <summary>派生索引列上的 json_extract 表达式索引（与 <see cref="KvJsonTable.EnsureJsonIndex"/> 同口径，作用列为 <c>[IndexJson]</c>）。</summary>
    public static void EnsureIndexJsonIndex(IDbCommandExecutor executor, string table, string indexName, string jsonPath, string? extra = null)
    {
        var suffix = extra is null ? "" : "," + extra;
        executor.ExecuteNonQuery(
            $"CREATE INDEX IF NOT EXISTS [{indexName}] ON [{table}](json_extract({ColIndexJson},'{jsonPath}'){suffix});");
    }
}
