using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>Sqlite Schema 初始化器：幂等创建 ②ur_tasks（状态集合，任务定义+运行时状态+调用的模型）。
/// 三库收敛后 SQL 仅 ur_tasks 一表；checkpoint/config/keymapping 与引用关系均经节点库 / LiteDB 图库承载，
/// 不再单独建 SQL ur_resources 表（引用关系真源为 LiteDB 图库 graph-log.db）。</summary>
public sealed class SqliteSchema : IDbSchemaInitializer
{
    private readonly IDbCommandExecutor _executor;
    private readonly string _prefix;
    private readonly ITableSchemaSource? _schemaSource;

    public SqliteSchema(IDbCommandExecutor executor, string tablePrefix, ITableSchemaSource? schemaSource = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _prefix = PersistenceHelper.ResolveTablePrefix(tablePrefix);
        _schemaSource = schemaSource;
    }

    public string SnapshotsTable => _prefix + TableNames.Tasks;

    public string TasksTable => _prefix + TableNames.Tasks;

    public string ConfigTable => _prefix + TableNames.Nodes;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // ② 状态集合 ur_tasks：**纯 USharp 三列** [Id]/[NodeKey]/[Doc]（与 ur_nodes 同构），
        //    真源 [Doc] = [Record] 实例规范文本（经 IDocumentCodec；注入 USharp codec ⇒ USharp）。
        //    ThreadId/SessionId/Status/Priority/MaxRetries/CreatedAt/UpdatedAt 全部随文档持久化；
        //    过滤/排序/CAS 经 usharp_get([Doc],'$.<field>') 表达式索引在 SQL 侧下推（集中式转换层 §1.6）。
        // 旧版四列 / 旧 KvJson 三列 / 四列 [IndexJson] 结构由各 Store 初始化时统一收敛（MigrateToPure）。
        // 表结构声明源（USharp [Table]）优先；缺件回落手写 KvUsharpTable.SchemaColumns（零行为变更）。
        var declared = _schemaSource?.GetColumns(TableNames.Tasks) ?? _schemaSource?.GetColumns("tasks");
        _executor.ExecuteNonQuery(declared is null
            ? KvUsharpTable.CreateSql(TasksTable)
            : PersistenceHelper.CreateTableSql(TasksTable, declared.ToArray()));
        return Task.CompletedTask;
    }
}