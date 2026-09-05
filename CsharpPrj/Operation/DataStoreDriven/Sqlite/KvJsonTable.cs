using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// 同构「业务键 NodeKey + 单个 Json」表（三库收敛后 nodes/tasks/histories 共用）的 SQL 模板收敛器。
/// 建表/索引/迁移/upsert/查询/删除的 DDL·DML 单一来源，消除各 Sqlite Store 重复手写的拼接字符串（SingleImplementation）。
/// </summary>
public static class KvJsonTable
{
    /// <summary>统一三列结构（全部元数据随 Json，NodeKey 唯一，Id 自增）。</summary>
    public static readonly string[] SchemaColumns =
    {
        "[Id] INTEGER PRIMARY KEY AUTOINCREMENT",
        "[NodeKey] TEXT NOT NULL UNIQUE",
        "[Json] TEXT NOT NULL",
    };

    public const string ColId = "[Id]";
    public const string ColNodeKey = "[NodeKey]";
    public const string ColJson = "[Json]";

    /// <summary>幂等建表 SQL。</summary>
    public static string CreateSql(string table) => PersistenceHelper.CreateTableSql(table, SchemaColumns);

    /// <summary>幂等建表（执行）。</summary>
    public static void EnsureCreated(IDbCommandExecutor executor, string table)
        => executor.ExecuteNonQuery(CreateSql(table));

    /// <summary>按 NodeKey 查询的列投影前缀（含 <c>LIMIT 1</c>；columns 缺省仅 Json）。ColNodeKey 已含方括号，勿再包裹。</summary>
    public static string SelectByKeySql(string table, string columns = "[Json]")
        => $"SELECT {columns} FROM [{table}] WHERE {ColNodeKey}=@k LIMIT 1";

    /// <summary>按 NodeKey 删除。</summary>
    public static string DeleteByKeySql(string table)
        => $"DELETE FROM [{table}] WHERE {ColNodeKey}=@k";

    /// <summary>NodeKey+Json upsert（幂等覆写 Json；onConflictUpdate 缺省整行覆写，可传 json_set 保留首写时间戳）。</summary>
    public static string UpsertSql(string table, string onConflictUpdate = "[Json]=@j")
        => $"INSERT INTO [{table}] ({ColNodeKey},{ColJson}) VALUES (@k,@j) ON CONFLICT({ColNodeKey}) DO UPDATE SET {onConflictUpdate}";

    /// <summary>JSON 表达式索引（SQL 常量须与查询逐字一致，统一经 <see cref="JsonExtract"/> 生成避免漂移）。extra 为附加排序列（如 [Id]）。</summary>
    public static void EnsureJsonIndex(IDbCommandExecutor executor, string table, string indexName, string jsonPath, string? extra = null)
    {
        var suffix = extra is null ? "" : "," + extra;
        executor.ExecuteNonQuery($"CREATE INDEX IF NOT EXISTS [{indexName}] ON [{table}]({JsonExtract(jsonPath)}{suffix});");
    }

    /// <summary>旧多列表迁移为纯键值表（整表重建为三列）。insertSelect 为 <c>SELECT [Id], &lt;NodeKey 表达式&gt;, &lt;Json 表达式&gt;</c>（FROM [旧表] 由本方法追加）。</summary>
    public static void MigrateLegacyToKvJson(IDbCommandExecutor executor, string table, string insertSelect)
    {
        var tmp = table + "_mig";
        executor.ExecuteNonQuery($"DROP TABLE IF EXISTS [{tmp}];");
        executor.ExecuteNonQuery($"CREATE TABLE [{tmp}] ({ColId} INTEGER PRIMARY KEY AUTOINCREMENT, {ColNodeKey} TEXT NOT NULL UNIQUE, {ColJson} TEXT NOT NULL);");
        executor.ExecuteNonQuery($"INSERT INTO [{tmp}] ({ColId},{ColNodeKey},{ColJson}) {insertSelect} FROM [{table}];");
        executor.ExecuteNonQuery($"DROP TABLE [{table}];");
        executor.ExecuteNonQuery($"ALTER TABLE [{tmp}] RENAME TO [{table}];");
    }

    /// <summary><c>json_extract([Json],'$.x')</c> 表达式（含单引号包裹，与表达式索引逐字一致）。ColJson 已含方括号，勿再包裹。</summary>
    public static string JsonExtract(string jsonPath) => $"json_extract({ColJson},'{jsonPath}')";

    /// <summary>引号包裹的 JSON 路径（供 json_set/json_object 第一参数使用）。</summary>
    public static string PathRef(string jsonPath) => $"'{jsonPath}'";

    /// <summary>JSON 路径常量（与表达式索引逐字一致；消除魔法字符串漂移）。</summary>
    public static class Path
    {
        public const string ThreadId = "$.ThreadId";
        public const string SessionId = "$.SessionId";
        public const string Status = "$.Status";
        public const string NodeType = "$.NodeType";
        public const string Priority = "$.Priority";
        public const string MaxRetries = "$.MaxRetries";
        public const string CreatedAt = "$.CreatedAt";
        public const string UpdatedAt = "$.UpdatedAt";
        public const string EnvelopeCreatedAt = "$.__createdAt";
        public const string EnvelopeUpdatedAt = "$.__updatedAt";
    }
}
