using System;
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

    public SqliteSchema(IDbCommandExecutor executor, string tablePrefix)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _prefix = PersistenceHelper.ResolveTablePrefix(tablePrefix);
    }

    public string SnapshotsTable => _prefix + TableNames.Tasks;

    public string TasksTable => _prefix + TableNames.Tasks;

    public string ConfigTable => _prefix + TableNames.Nodes;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // ② 状态集合 ur_tasks：纯「业务键（NodeKey）+ 一个 Json」——与 ur_nodes 同构，统一承载快照根 / 任务池条目 / 会话快照（任务 = 状态根）。
        // ThreadId/SessionId/Status/Priority/MaxRetries/CreatedAt/UpdatedAt 全部随 Json 持久化（过滤由 JSON 表达式索引 json_extract 支撑）。
        // 旧版 ThreadId/SessionId/SnapshotJson 四列结构由 SqliteTaskPoolStore 初始化时统一迁移收敛（SingleImplementation）。
        _executor.ExecuteNonQuery(KvJsonTable.CreateSql(TasksTable));
        return Task.CompletedTask;
    }
}