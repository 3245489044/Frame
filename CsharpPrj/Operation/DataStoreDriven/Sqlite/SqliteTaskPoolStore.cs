using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.TaskStatusPool.TaskPool;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// Sqlite 任务池持久化存储（②状态集合 ur_tasks 单表，实现 <see cref="ITaskPoolPersistence{TItem}"/>）。
/// 任务 = 状态根：TaskKey 作为 NodeKey（ThreadId 命名空间 "taskpool" 随文档持久化，不再单独成列，与 ur_nodes 同构）。
/// 真源列 <c>[Doc]</c>（文本，经 <see cref="IDocumentCodec"/> 编解码；注入 USharp codec ⇒ USharp 文本）+ 派生索引列 <c>[IndexJson]</c>（JSON）；
/// ThreadId/Status/Priority/MaxRetries/__createdAt/__updatedAt 为文档顶层键，过滤由 <c>json_extract([IndexJson])</c> 支撑。
/// 部分更新（CAS/载荷覆写）走「读改写」（json_* 无法作用于 USharp 文本）。
/// 任务的相关节点（输入/输出等）经 <see cref="ISnapshotGraphStore"/> 的引用边挂到同一状态根下（任务 = 数据根）。
/// </summary>
public sealed class SqliteTaskPoolStore<TItem> : KvJsonStoreBase, ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    /// <summary>任务池状态根的固定 ThreadId 命名空间（随文档持久化；NodeKey=TaskKey 全局唯一）。</summary>
    public const string PoolThreadId = "taskpool";

    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IDocumentCodec _codec;

    public SqliteTaskPoolStore(IDbCommandExecutor executor, string tablePrefix, JsonSerializerOptions? jsonOptions = null, IDocumentCodec? codec = null)
        : base(executor, tablePrefix, TableNames.Tasks)
    {
        _jsonOptions = jsonOptions ?? PersistenceHelper.JsonOptions;
        _codec = codec ?? JsonDocumentCodec.Instance;
    }

    public string TableName => Table;

    /// <summary>幂等建表：真源列 <c>[Doc]</c> + 派生索引列 <c>[IndexJson]</c>（与 ur_nodes 同构）；
    /// ThreadId/Status/Priority 过滤由 <c>json_extract([IndexJson])</c> 表达式索引支撑。
    /// 旧版四列 <see cref="MigrateLegacySchema"/> / 旧 KvJson 三列 <see cref="KvDocumentTable.MigrateFromKvJson"/> 自动收敛。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 旧版 ThreadId/SessionId/SnapshotJson 四列结构先折入 KvJson 三列（整表重建；仅旧库命中，幂等）。
        MigrateLegacySchema();
        // 数据即声明：真源列 [Doc]（USharp 文本）+ 派生索引列 [IndexJson]；旧 KvJson 三列就地迁移。
        KvDocumentTable.EnsureCreated(Executor, Table);
        KvDocumentTable.MigrateFromKvJson(Executor, Table);
        // 派生列上的 json_extract 表达式索引（不新增物理列）。
        KvDocumentTable.EnsureIndexJsonIndex(Executor, Table, "idx_tasks_status", KvJsonTable.Path.Status);
        KvDocumentTable.EnsureIndexJsonIndex(Executor, Table, "idx_tasks_priority", KvJsonTable.Path.Priority);
        return Task.CompletedTask;
    }

    /// <summary>旧版 ur_tasks（ThreadId/SessionId/SnapshotJson 四列，UNIQUE(ThreadId,SessionId)）数据折入 Json 并重建表；仅旧库命中，幂等。
    /// ThreadId/SessionId 参与旧 UNIQUE 约束不可 DROP COLUMN，故整表重建（NodeKey=SessionId，ThreadId/SessionId 折入 Json）。
    /// 注：迁移后任务池与快照行以 Json 内 ThreadId 键区分（NodeKey 各取自 SessionId，同库共存由 ThreadId 键隔离，查询均按 ThreadId 过滤）。</summary>
    private void MigrateLegacySchema()
    {
        var cols = Query($"PRAGMA table_info([{Table}])");
        var names = cols.Select(r => SqliteRows.Str(r, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("ThreadId") && !names.Contains("SnapshotJson"))
            return; // 已是纯键值表（NodeKey+Json），无需迁移。
        KvJsonTable.MigrateLegacyToKvJson(Executor, Table,
            $"SELECT [Id],[SessionId],json_set([SnapshotJson],{KvJsonTable.PathRef(KvJsonTable.Path.ThreadId)},[ThreadId],{KvJsonTable.PathRef(KvJsonTable.Path.SessionId)},[SessionId])");
    }

    public Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();
        var now = PersistenceHelper.NowIso();
        // 读改写：保留既有 __createdAt（首次写入取 now）；Status 快照驱动取载荷内值，否则状态机初始 NotStarted。
        var createdAt = ReadDoc(item.TaskKey)?["__createdAt"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(createdAt)) createdAt = now;
        var status = item is IStatusAwareTaskPoolItem sa ? sa.Status.ToString() : TaskExecutionStatus.NotStarted.ToString();
        var doc = BuildDoc(item, createdAt, now, status);
        var (text, indexJson) = Materialize(doc);
        var id = InsertReturningId(KvDocumentTable.UpsertSql(Table), Params(("k", item.TaskKey), ("d", text), ("ix", indexJson)));
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(
            $"SELECT {KvDocumentTable.ColDoc} FROM [{Table}] WHERE {IndexExtract(KvJsonTable.Path.ThreadId)}=@t AND {IndexExtract(KvJsonTable.Path.Status)}=@s ORDER BY {KvDocumentTable.ColId} ASC",
            Params(("t", PoolThreadId), ("s", status.ToString())));
        // 出队优先级排序经反序列化 item.Priority（文档内同源）；出队语义保持。
        var list = new List<TItem>(rows.Count);
        foreach (var row in rows)
        {
            var item = Deserialize(row);
            if (item is not null) list.Add(item);
        }
        list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        return Task.FromResult<IReadOnlyList<TItem>>(list.Take(Math.Max(0, limit)).ToList());
    }

    public Task<TItem?> GetAsync(string taskKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(
            $"SELECT {KvDocumentTable.ColDoc} FROM [{Table}] WHERE {KvDocumentTable.ColNodeKey}=@k AND {IndexExtract(KvJsonTable.Path.ThreadId)}=@t LIMIT 1",
            Params(("k", taskKey), ("t", PoolThreadId)));
        return Task.FromResult(rows.Count > 0 ? Deserialize(rows[0]) : default);
    }

    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 读改写：读真源 [Doc] → 改 Status/__updatedAt → 一次 UPDATE 写回 [Doc]+[IndexJson]；
        // CAS 原子性由 WHERE 守卫（json_extract([IndexJson],'$.Status')=@from）保证（竞争下不误改）。
        var doc = ReadDoc(taskKey);
        if (doc is null) return Task.FromResult(false);
        doc["Status"] = to.ToString();
        doc["__updatedAt"] = PersistenceHelper.NowIso();
        var (text, indexJson) = Materialize(doc);
        var affected = ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvDocumentTable.ColDoc}=@d,{KvDocumentTable.ColIndexJson}=@ix " +
            $"WHERE {KvDocumentTable.ColNodeKey}=@k AND {IndexExtract(KvJsonTable.Path.ThreadId)}=@t AND {IndexExtract(KvJsonTable.Path.Status)}=@from",
            Params(
                ("d", text),
                ("ix", indexJson),
                ("k", taskKey),
                ("t", PoolThreadId),
                ("from", from.ToString())));
        return Task.FromResult(affected > 0);
    }

    /// <summary>更新载荷（含运行时快照字段）+ 刷新 __updatedAt；ThreadId/Status/Priority/MaxRetries 随 TItem 序列化同步。
    /// 非快照驱动条目（无 Status 属性）的 Status 保留既有值（文档内单源）；__createdAt 保留。</summary>
    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();
        // 读改写：读真源 [Doc] 取既有 Status/__createdAt，再以 TItem 序列化覆盖并保留缺省字段。
        var existing = ReadDoc(taskKey);
        var status = item is IStatusAwareTaskPoolItem sa
            ? sa.Status.ToString()
            : existing?["Status"]?.GetValue<string>() ?? TaskExecutionStatus.NotStarted.ToString();
        var createdAt = existing?["__createdAt"]?.GetValue<string>() ?? PersistenceHelper.NowIso();
        var doc = BuildDoc(item, createdAt, PersistenceHelper.NowIso(), status);
        var (text, indexJson) = Materialize(doc);
        ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvDocumentTable.ColDoc}=@d,{KvDocumentTable.ColIndexJson}=@ix " +
            $"WHERE {KvDocumentTable.ColNodeKey}=@k AND {IndexExtract(KvJsonTable.Path.ThreadId)}=@t",
            Params(("d", text), ("ix", indexJson), ("k", taskKey), ("t", PoolThreadId)));
        return Task.CompletedTask;
    }

    /// <summary>删除一条任务（持久化清理）：DELETE 该 ur_tasks 状态根行。返回是否命中删除（不存在返回 false）。</summary>
    public Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var affected = ExecuteNonQuery(
            $"DELETE FROM [{Table}] WHERE {KvDocumentTable.ColNodeKey}=@k AND {IndexExtract(KvJsonTable.Path.ThreadId)}=@t",
            Params(("k", taskKey), ("t", PoolThreadId)));
        return Task.FromResult(affected > 0);
    }

    /// <summary>分页读取任务（浏览/导出用）：返回一页 <typeparamref name="TItem"/> 与总数（COUNT 单独查询，LIMIT/OFFSET 下推 SQL）。status 非空时按状态过滤。</summary>
    public Task<PagedTaskPool<TItem>> GetPageAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (where, ps) = BuildPoolWhere(status);
        var total = Scalar($"SELECT COUNT(*) FROM [{Table}] WHERE {where}", ps);
        ps["@lim"] = Math.Max(0, pageSize);
        ps["@off"] = Math.Max(0, offset);
        var rows = Query(
            $"SELECT {KvDocumentTable.ColDoc} FROM [{Table}] WHERE {where} ORDER BY json_extract({KvDocumentTable.ColIndexJson},{KvJsonTable.PathRef(KvJsonTable.Path.Priority)}) DESC, {KvDocumentTable.ColId} ASC LIMIT @lim OFFSET @off",
            ps);
        var items = new List<TItem>(rows.Count);
        foreach (var row in rows)
        {
            var item = Deserialize(row);
            if (item is not null) items.Add(item);
        }
        return Task.FromResult(new PagedTaskPool<TItem>(items, total is null ? 0 : Convert.ToInt32(total, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>分页读取原始行视图（浏览 UI / md 渲染用）：含 Id/CreatedAt/UpdatedAt 与原始 Json，展示全字段。
    /// Priority/MaxRetries/Status/CreatedAt/UpdatedAt 经 json_extract 从 Json 派生（镜像列已收敛）。总数经 COUNT 查询（修复旧版仅返回页内条数）。</summary>
    public Task<PagedTaskPool<TaskPoolRowView>> GetPageRowsAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (where, ps) = BuildPoolWhere(status);
        var select =
            $"SELECT {KvDocumentTable.ColId},{KvDocumentTable.ColNodeKey},{KvDocumentTable.ColDoc}, {IndexExtract(KvJsonTable.Path.Priority)} AS [Priority], {IndexExtract(KvJsonTable.Path.MaxRetries)} AS [MaxRetries], {IndexExtract(KvJsonTable.Path.Status)} AS [Status], {IndexExtract(KvJsonTable.Path.EnvelopeCreatedAt)} AS [CreatedAt], {IndexExtract(KvJsonTable.Path.EnvelopeUpdatedAt)} AS [UpdatedAt] FROM [{Table}]";
        var total = Scalar($"SELECT COUNT(*) FROM [{Table}] WHERE {where}", ps);
        ps["@lim"] = Math.Max(0, pageSize);
        ps["@off"] = Math.Max(0, offset);
        var rows = Query($"{select} WHERE {where} ORDER BY {KvJsonTable.ColId} ASC LIMIT @lim OFFSET @off", ps);
        var views = new List<TaskPoolRowView>(rows.Count);
        foreach (var row in rows)
        {
            views.Add(new TaskPoolRowView(
                SqliteRows.Long(row, "Id"),
                SqliteRows.Str(row, "NodeKey"),
                (int)SqliteRows.Long(row, "Priority", 0),
                (int)SqliteRows.Long(row, "MaxRetries", 0),
                SqliteRows.Nullable(row, "Status") ?? string.Empty,
                SqliteRows.Nullable(row, "Doc"),
                SqliteRows.Nullable(row, "CreatedAt"),
                SqliteRows.Nullable(row, "UpdatedAt")));
        }
        return Task.FromResult(new PagedTaskPool<TaskPoolRowView>(views, total is null ? 0 : Convert.ToInt32(total, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>任务池行过滤的公共 WHERE（固定 ThreadId 命名空间 + 可选 Status）与参数字典。</summary>
    private (string Where, Dictionary<string, object> Ps) BuildPoolWhere(TaskExecutionStatus? status)
    {
        var where = $"{IndexExtract(KvJsonTable.Path.ThreadId)}=@t";
        var ps = Params(("t", PoolThreadId));
        if (status is not null) { where += $" AND {IndexExtract(KvJsonTable.Path.Status)}=@s"; ps["@s"] = status.Value.ToString(); }
        return (where, ps);
    }

    /// <summary>派生索引列 [IndexJson] 上的 json_extract 表达式（与建索引时逐字一致）。</summary>
    private static string IndexExtract(string jsonPath)
        => $"json_extract({KvDocumentTable.ColIndexJson},'{jsonPath}')";

    /// <summary>构造任务文档对象（TItem 序列化 + ThreadId/Status/__createdAt/__updatedAt 顶层键）；非对象载荷（异常）以空对象兜底。
    /// 消费方（如 <see cref="AgentTaskItemJsonConverter"/>）的 Read 只读已知键，未知键（__createdAt/__updatedAt，及非快照驱动条目的 Status/ThreadId）自动忽略，反序列化零影响。</summary>
    private JsonObject BuildDoc(TItem item, string createdAt, string updatedAt, string status)
    {
        var obj = JsonSerializer.SerializeToNode(item, _jsonOptions) as JsonObject ?? new JsonObject();
        obj["ThreadId"] = PoolThreadId;
        obj["Status"] = status;
        obj["__createdAt"] = createdAt;
        obj["__updatedAt"] = updatedAt;
        return obj;
    }

    /// <summary>由文档对象产真源 [Doc]（经 codec）与派生 [IndexJson]（同构 JSON）。</summary>
    private (string Doc, string Index) Materialize(JsonObject doc)
        => (_codec.Serialize(doc), doc.ToJsonString(_jsonOptions));

    /// <summary>按 NodeKey 读真源 [Doc] 并还原为文档对象；不存在/解析失败 ⇒ null。</summary>
    private JsonObject? ReadDoc(string taskKey)
    {
        var rows = Query(KvDocumentTable.SelectByKeySql(Table, KvDocumentTable.ColDoc), Params(("k", taskKey)));
        return rows.Count == 0 ? null : _codec.Deserialize<JsonObject>(SqliteRows.Str(rows[0], "Doc"));
    }

    private TItem? Deserialize(IReadOnlyDictionary<string, object> row)
    {
        var doc = SqliteRows.Str(row, "Doc");
        if (string.IsNullOrWhiteSpace(doc)) return default;
        // 单行载荷损坏不阻断整批列出（codec 内部容错 ⇒ null），交由任务池失败重试语义兜底。
        return _codec.Deserialize<TItem>(doc);
    }
}