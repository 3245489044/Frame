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
/// 任务 = 状态根：TaskKey 作为 NodeKey（ThreadId 命名空间 "taskpool" 随 Json 持久化，不再单独成列，与 ur_nodes 同构）。
/// 表收敛为纯「业务键（NodeKey）+ 一个 Json」——载荷（TItem）序列化为 Json 即唯一权威状态字符串，
/// ThreadId/Status/Priority/MaxRetries/CreatedAt/UpdatedAt 全部随 Json 持久化（ThreadId/Status/Priority/MaxRetries 为 Json 顶层键，
/// CreatedAt/UpdatedAt 以 __createdAt/__updatedAt 顶层键注入），过滤由 JSON 表达式索引（json_extract）支撑。
/// 任务的相关节点（输入/输出等）经 <see cref="ISnapshotGraphStore"/> 的引用边挂到同一状态根下（任务 = 数据根）。
/// </summary>
public sealed class SqliteTaskPoolStore<TItem> : KvJsonStoreBase, ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    /// <summary>任务池状态根的固定 ThreadId 命名空间（随 Json 持久化；NodeKey=TaskKey 全局唯一）。</summary>
    public const string PoolThreadId = "taskpool";

    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteTaskPoolStore(IDbCommandExecutor executor, string tablePrefix, JsonSerializerOptions? jsonOptions = null)
        : base(executor, tablePrefix, TableNames.Tasks)
    {
        _jsonOptions = jsonOptions ?? PersistenceHelper.JsonOptions;
    }

    public string TableName => Table;

    /// <summary>幂等建表：纯「业务键（NodeKey）+ 一个 Json」（与 ur_nodes 同构）；ThreadId/Status/Priority 过滤由 JSON 表达式索引（json_extract）支撑。
    /// 旧版 ThreadId/SessionId/SnapshotJson 四列自动迁移收敛（<see cref="MigrateLegacySchema"/>）。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureCreated();
        // 旧版 ThreadId/SessionId/SnapshotJson 四列结构折入 Json 并重建表（仅旧库命中，幂等）。
        MigrateLegacySchema();
        // JSON 表达式索引支撑按 Status 过滤 / Priority 排序（不新增物理列，SQL 常量经 KvJsonTable.JsonExtract 统一生成）。
        KvJsonTable.EnsureJsonIndex(Executor, Table, "idx_tasks_status", KvJsonTable.Path.Status);
        KvJsonTable.EnsureJsonIndex(Executor, Table, "idx_tasks_priority", KvJsonTable.Path.Priority);
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
        // Json = 整条 TItem 序列化 + ThreadId + Status（快照驱动取载荷内 Status，否则状态机初始 NotStarted）+ __createdAt/__updatedAt 顶层键。
        var status = item is IStatusAwareTaskPoolItem sa ? sa.Status.ToString() : TaskExecutionStatus.NotStarted.ToString();
        var json = InjectEnvelope(JsonSerializer.Serialize(item, _jsonOptions), now, now, status);
        var conflictUpdate =
            $"{KvJsonTable.ColJson}=json_set(excluded.{KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeCreatedAt)},COALESCE(json_extract({KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeCreatedAt)}),json_extract(excluded.{KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeCreatedAt)})))";
        var id = InsertReturningId(KvJsonTable.UpsertSql(Table, conflictUpdate), Params(("k", item.TaskKey), ("j", json)));
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(
            $"SELECT {KvJsonTable.ColJson} FROM [{Table}] WHERE {KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t AND {KvJsonTable.JsonExtract(KvJsonTable.Path.Status)}=@s ORDER BY {KvJsonTable.ColId} ASC",
            Params(("t", PoolThreadId), ("s", status.ToString())));
        // 出队优先级排序经反序列化 item.Priority（Json 内同源）；出队语义保持。
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
            $"SELECT {KvJsonTable.ColJson} FROM [{Table}] WHERE {KvJsonTable.ColNodeKey}=@k AND {KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t LIMIT 1",
            Params(("k", taskKey), ("t", PoolThreadId)));
        return Task.FromResult(rows.Count > 0 ? Deserialize(rows[0]) : default);
    }

    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // CAS 条件迁移：Status 本就是 Json 内单源，json_set 一处更新即完成（无需再二次同步载荷）。
        var affected = ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvJsonTable.ColJson}=json_set({KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.Status)},@to,{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeUpdatedAt)},@u) " +
            $"WHERE {KvJsonTable.ColNodeKey}=@k AND {KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t AND {KvJsonTable.JsonExtract(KvJsonTable.Path.Status)}=@from",
            Params(
                ("to", to.ToString()),
                ("u", PersistenceHelper.NowIso()),
                ("k", taskKey),
                ("t", PoolThreadId),
                ("from", from.ToString())));
        return Task.FromResult(affected > 0);
    }

    /// <summary>更新载荷 Json（含运行时快照字段）+ 刷新 __updatedAt；ThreadId/Status/Priority/MaxRetries 随 TItem 序列化同步。
    /// 非快照驱动条目（无 Status 属性）的 Status 保留既有值（json 内单源）；__createdAt 保留（json_patch 合并）。</summary>
    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(item, _jsonOptions);
        ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvJsonTable.ColJson}=json_patch(@j,json_object({KvJsonTable.PathRef(KvJsonTable.Path.ThreadId)},@t,{KvJsonTable.PathRef(KvJsonTable.Path.Status)},COALESCE(json_extract(@j,{KvJsonTable.PathRef(KvJsonTable.Path.Status)}),json_extract({KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.Status)}),'NotStarted'),{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeCreatedAt)},COALESCE(json_extract({KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeCreatedAt)}),json_extract(@j,{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeCreatedAt)})),{KvJsonTable.PathRef(KvJsonTable.Path.EnvelopeUpdatedAt)},@u)) " +
            $"WHERE {KvJsonTable.ColNodeKey}=@k AND {KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t",
            Params(("j", json), ("u", PersistenceHelper.NowIso()), ("k", taskKey), ("t", PoolThreadId)));
        return Task.CompletedTask;
    }

    /// <summary>删除一条任务（持久化清理）：DELETE 该 ur_tasks 状态根行。返回是否命中删除（不存在返回 false）。</summary>
    public Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var affected = ExecuteNonQuery(
            $"DELETE FROM [{Table}] WHERE {KvJsonTable.ColNodeKey}=@k AND {KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t",
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
            $"SELECT {KvJsonTable.ColJson} FROM [{Table}] WHERE {where} ORDER BY json_extract({KvJsonTable.ColJson},{KvJsonTable.PathRef(KvJsonTable.Path.Priority)}) DESC, {KvJsonTable.ColId} ASC LIMIT @lim OFFSET @off",
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
            $"SELECT {KvJsonTable.ColId},{KvJsonTable.ColNodeKey},{KvJsonTable.ColJson}, {KvJsonTable.JsonExtract(KvJsonTable.Path.Priority)} AS [Priority], {KvJsonTable.JsonExtract(KvJsonTable.Path.MaxRetries)} AS [MaxRetries], {KvJsonTable.JsonExtract(KvJsonTable.Path.Status)} AS [Status], {KvJsonTable.JsonExtract(KvJsonTable.Path.EnvelopeCreatedAt)} AS [CreatedAt], {KvJsonTable.JsonExtract(KvJsonTable.Path.EnvelopeUpdatedAt)} AS [UpdatedAt] FROM [{Table}]";
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
                SqliteRows.Nullable(row, "Json"),
                SqliteRows.Nullable(row, "CreatedAt"),
                SqliteRows.Nullable(row, "UpdatedAt")));
        }
        return Task.FromResult(new PagedTaskPool<TaskPoolRowView>(views, total is null ? 0 : Convert.ToInt32(total, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>任务池行过滤的公共 WHERE（固定 ThreadId 命名空间 + 可选 Status）与参数字典。</summary>
    private (string Where, Dictionary<string, object> Ps) BuildPoolWhere(TaskExecutionStatus? status)
    {
        var where = $"{KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t";
        var ps = Params(("t", PoolThreadId));
        if (status is not null) { where += $" AND {KvJsonTable.JsonExtract(KvJsonTable.Path.Status)}=@s"; ps["@s"] = status.Value.ToString(); }
        return (where, ps);
    }

    /// <summary>给序列化后的 Json（扁平对象）注入 ThreadId + Status + __createdAt/__updatedAt 顶层键；非对象（异常载荷）原样返回。
    /// 消费方（如 <see cref="AgentTaskItemJsonConverter"/>）的 Read 只读已知键，未知键（__createdAt/__updatedAt，及非快照驱动条目的 Status/ThreadId）自动忽略，反序列化零影响。</summary>
    private string InjectEnvelope(string json, string createdAt, string updatedAt, string status)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj) return json;
        obj["ThreadId"] = PoolThreadId;
        obj["Status"] = status;
        obj["__createdAt"] = createdAt;
        obj["__updatedAt"] = updatedAt;
        return obj.ToJsonString(_jsonOptions);
    }

    private TItem? Deserialize(IReadOnlyDictionary<string, object> row)
    {
        var json = SqliteRows.Nullable(row, "Json");
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            return JsonSerializer.Deserialize<TItem>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return default; // 单行载荷损坏不阻断整批列出，交由任务池失败重试语义兜底
        }
    }
}