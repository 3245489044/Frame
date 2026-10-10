using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.TaskStatusPool.TaskPool;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// Sqlite 任务池持久化存储（②状态集合 ur_tasks 单表，实现 <see cref="ITaskPoolPersistence{TItem}"/>）。
/// 任务 = 状态根：TaskKey 作为 NodeKey（ThreadId 命名空间 "taskpool" 随文档持久化）。
/// <para>
/// 表为**纯 USharp 三列** <c>[Id]/[NodeKey]/[Doc]</c>；真源 <c>[Doc]</c> =
/// <c>[Record] task:&lt;TaskKey&gt;: table=Tasks | key=&lt;TaskKey&gt;</c> 实例规范文本（经 <see cref="IDocumentCodec"/>，注入 USharp codec ⇒ USharp）。
/// ThreadId/Status/__createdAt/__updatedAt 为记录字段（与 TItem 字段扁平同层）；过滤/排序/分页/CAS 经
/// <c>usharp_get([Doc],'$.&lt;field&gt;')</c> 表达式索引在 SQL 侧下推（集中式转换层，施工单 §1.6）——非内存全表解码。
/// </para>
/// </summary>
public sealed class SqliteTaskPoolStore<TItem> : KvJsonStoreBase, ITaskPoolPersistence<TItem>
    where TItem : ITaskPoolItem
{
    /// <summary>任务池状态根的固定 ThreadId 命名空间（随文档持久化；NodeKey=TaskKey 全局唯一）。</summary>
    public const string PoolThreadId = "taskpool";

    private const string TableId = "Tasks";
    private const string FieldThreadId = "ThreadId";
    private const string FieldStatus = "Status";
    private const string FieldCreatedAt = "__createdAt";
    private const string FieldUpdatedAt = "__updatedAt";

    private readonly IDocumentCodec _codec;

    /// <summary>注入命令执行器 + 表前缀。编解码缺省 = JSON；注入 USharp codec ⇒ 文档 USharp 化。
    /// <paramref name="jsonOptions"/> 保留以兼容旧装配签名（记录实例路径不再经 System.Text.Json）。</summary>
    public SqliteTaskPoolStore(IDbCommandExecutor executor, string tablePrefix, JsonSerializerOptions? jsonOptions = null, IDocumentCodec? codec = null)
        : base(executor, tablePrefix, TableNames.Tasks)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec), "ur_tasks 记录型存储须注入 IDocumentCodec（USharp codec）。");
    }

    public string TableName => Table;

    /// <summary>幂等建表：**纯三列** <c>[Id]/[NodeKey]/[Doc]</c>（与 ur_nodes 同构）。
    /// 旧结构（四列 <c>[IndexJson]</c> / 旧 <c>[Json]</c> / 更老多列）按无视删除口径丢弃重建；
    /// 过滤/排序键经 <c>usharp_get</c> 表达式索引在 SQL 侧下推。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        KvUsharpTable.ResetToPureIfNeeded(Executor, Table);
        KvUsharpTable.EnsureCreated(Executor, Table);
        KvUsharpTable.EnsureUsharpIndex(Executor, Table, "idx_tasks_status", UsharpRecordPaths.Status);
        KvUsharpTable.EnsureUsharpIndex(Executor, Table, "idx_tasks_priority", UsharpRecordPaths.Priority);
        return Task.CompletedTask;
    }

    public Task<long> EnqueueAsync(TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();
        var now = PersistenceHelper.NowIso();
        // 读改写：保留既有 __createdAt（首次写入取 now）；Status 快照驱动取载荷内值，否则状态机初始 NotStarted。
        var existing = ReadEnvelope(item.TaskKey);
        var createdAt = string.IsNullOrWhiteSpace(existing.CreatedAt) ? now : existing.CreatedAt!;
        var status = item is IStatusAwareTaskPoolItem sa ? sa.Status.ToString() : TaskExecutionStatus.NotStarted.ToString();
        var text = _codec.SerializeRecord(RecordId(item.TaskKey), TableId, item.TaskKey, item, Envelope(status, createdAt, now));
        var id = InsertReturningId(KvUsharpTable.UpsertSql(Table), Params(("k", item.TaskKey), ("d", text)));
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<TItem>> ListAsync(TaskExecutionStatus status, int limit = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 过滤（ThreadId/Status）与优先级排序（CAST 数字）均 SQL 侧下推（usharp_get 表达式）。
        var rows = Query(
            $"SELECT {KvUsharpTable.ColDoc} FROM [{Table}] WHERE {Extract(UsharpRecordPaths.ThreadId)}=@t AND {Extract(UsharpRecordPaths.Status)}=@s " +
            $"ORDER BY CAST({Extract(UsharpRecordPaths.Priority)} AS INTEGER) DESC",
            Params(("t", PoolThreadId), ("s", status.ToString())));
        var list = new List<TItem>(rows.Count);
        foreach (var row in rows)
        {
            if (TryReadItem(SqliteRows.Str(row, "Doc"), out var item)) list.Add(item!);
        }
        return Task.FromResult<IReadOnlyList<TItem>>(list.Take(Math.Max(0, limit)).ToList());
    }

    public Task<TItem?> GetAsync(string taskKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(
            $"SELECT {KvUsharpTable.ColDoc} FROM [{Table}] WHERE {KvUsharpTable.ColNodeKey}=@k AND {Extract(UsharpRecordPaths.ThreadId)}=@t LIMIT 1",
            Params(("k", taskKey), ("t", PoolThreadId)));
        return Task.FromResult(rows.Count > 0 && TryReadItem(SqliteRows.Str(rows[0], "Doc"), out var item) ? item : default);
    }

    public Task<bool> UpdateStatusAsync(string taskKey, TaskExecutionStatus from, TaskExecutionStatus to, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 读改写：读真源 [Doc] 的对象体与头字段 → 改 Status/__updatedAt → 写回 [Doc]；
        // CAS 原子性由 WHERE 守卫（usharp_get([Doc],'$.Status')=@from）保证（竞争下不误改，单语句原子）。
        var text = ReadDocText(taskKey);
        if (text is null) return Task.FromResult(false);
        if (!_codec.TryDeserializeRecord<TItem>(text, out var item) || item is null) return Task.FromResult(false);
        if (!_codec.TryDeserializeRecord(text, out _, out _, out _, out var body)) return Task.FromResult(false);
        var createdAt = Str(body, FieldCreatedAt) ?? PersistenceHelper.NowIso();
        var updated = _codec.SerializeRecord(RecordId(taskKey), TableId, taskKey, item, Envelope(to.ToString(), createdAt, PersistenceHelper.NowIso()));
        var affected = ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvUsharpTable.ColDoc}=@d " +
            $"WHERE {KvUsharpTable.ColNodeKey}=@k AND {Extract(UsharpRecordPaths.ThreadId)}=@t AND {Extract(UsharpRecordPaths.Status)}=@from",
            Params(("d", updated), ("k", taskKey), ("t", PoolThreadId), ("from", from.ToString())));
        return Task.FromResult(affected > 0);
    }

    /// <summary>更新载荷（含运行时快照字段）+ 刷新 __updatedAt；ThreadId/Status/Priority/MaxRetries 随 TItem 序列化同步。
    /// 非快照驱动条目（无 Status 属性）的 Status 保留既有值（文档内单源）；__createdAt 保留。</summary>
    public Task UpdatePayloadAsync(string taskKey, TItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ct.ThrowIfCancellationRequested();
        var existing = ReadEnvelope(taskKey);
        var status = item is IStatusAwareTaskPoolItem sa ? sa.Status.ToString() : existing.Status ?? TaskExecutionStatus.NotStarted.ToString();
        var createdAt = existing.CreatedAt ?? PersistenceHelper.NowIso();
        var text = _codec.SerializeRecord(RecordId(taskKey), TableId, taskKey, item, Envelope(status, createdAt, PersistenceHelper.NowIso()));
        ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvUsharpTable.ColDoc}=@d WHERE {KvUsharpTable.ColNodeKey}=@k AND {Extract(UsharpRecordPaths.ThreadId)}=@t",
            Params(("d", text), ("k", taskKey), ("t", PoolThreadId)));
        return Task.CompletedTask;
    }

    /// <summary>删除一条任务（持久化清理）：DELETE 该 ur_tasks 状态根行。返回是否命中删除（不存在返回 false）。</summary>
    public Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var affected = ExecuteNonQuery(
            $"DELETE FROM [{Table}] WHERE {KvUsharpTable.ColNodeKey}=@k AND {Extract(UsharpRecordPaths.ThreadId)}=@t",
            Params(("k", taskKey), ("t", PoolThreadId)));
        return Task.FromResult(affected > 0);
    }

    /// <summary>分页读取任务（浏览/导出用）：返回一页 <typeparamref name="TItem"/> 与总数。status 非空时按状态过滤；排序/过滤/分页 SQL 侧下推。</summary>
    public Task<PagedTaskPool<TItem>> GetPageAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (where, ps) = BuildPoolWhere(status);
        var total = Scalar($"SELECT COUNT(*) FROM [{Table}] WHERE {where}", ps);
        ps["@lim"] = Math.Max(0, pageSize);
        ps["@off"] = Math.Max(0, offset);
        var rows = Query(
            $"SELECT {KvUsharpTable.ColDoc} FROM [{Table}] WHERE {where} " +
            $"ORDER BY CAST({Extract(UsharpRecordPaths.Priority)} AS INTEGER) DESC, {KvUsharpTable.ColId} ASC LIMIT @lim OFFSET @off",
            ps);
        var items = new List<TItem>(rows.Count);
        foreach (var row in rows)
        {
            if (TryReadItem(SqliteRows.Str(row, "Doc"), out var item)) items.Add(item!);
        }
        return Task.FromResult(new PagedTaskPool<TItem>(items, total is null ? 0 : Convert.ToInt32(total, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>分页读取原始行视图（浏览 UI / md 渲染用）：含 Id/CreatedAt/UpdatedAt 与原始文档，展示全字段。
    /// Priority/MaxRetries/Status/CreatedAt/UpdatedAt 经 usharp_get 从 [Doc] 派生（无镜像列）。总数经 COUNT 查询。</summary>
    public Task<PagedTaskPool<TaskPoolRowView>> GetPageRowsAsync(int offset, int pageSize, TaskExecutionStatus? status = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (where, ps) = BuildPoolWhere(status);
        var select =
            $"SELECT {KvUsharpTable.ColId},{KvUsharpTable.ColNodeKey},{KvUsharpTable.ColDoc}, " +
            $"{Extract(UsharpRecordPaths.Priority)} AS [Priority], {Extract(UsharpRecordPaths.MaxRetries)} AS [MaxRetries], " +
            $"{Extract(UsharpRecordPaths.Status)} AS [Status], {Extract(UsharpRecordPaths.EnvelopeCreatedAt)} AS [CreatedAt], " +
            $"{Extract(UsharpRecordPaths.EnvelopeUpdatedAt)} AS [UpdatedAt] FROM [{Table}]";
        var total = Scalar($"SELECT COUNT(*) FROM [{Table}] WHERE {where}", ps);
        ps["@lim"] = Math.Max(0, pageSize);
        ps["@off"] = Math.Max(0, offset);
        var rows = Query($"{select} WHERE {where} ORDER BY {KvUsharpTable.ColId} ASC LIMIT @lim OFFSET @off", ps);
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

    // ---- 内部：记录实例读写 + usharp_get 下推 ----

    /// <summary>记录 ID（<c>task:&lt;TaskKey&gt;</c>）。</summary>
    private static string RecordId(string taskKey) => "task:" + taskKey;

    /// <summary>记录头字段（与 TItem 字段扁平同层，供 usharp_get 过滤/排序）。</summary>
    private static IReadOnlyList<KeyValuePair<string, UsharpValue>> Envelope(string status, string createdAt, string updatedAt) => new[]
    {
        new KeyValuePair<string, UsharpValue>(FieldThreadId, UsharpScalar.Str(PoolThreadId)),
        new KeyValuePair<string, UsharpValue>(FieldStatus, UsharpScalar.Str(status)),
        new KeyValuePair<string, UsharpValue>(FieldCreatedAt, UsharpScalar.Str(createdAt)),
        new KeyValuePair<string, UsharpValue>(FieldUpdatedAt, UsharpScalar.Str(updatedAt)),
    };

    /// <summary>usharp_get 表达式（与表达式索引逐字一致）。</summary>
    private static string Extract(string path) => KvUsharpTable.UsharpExtract(path);

    /// <summary>任务池行过滤的公共 WHERE（固定 ThreadId 命名空间 + 可选 Status）与参数字典。</summary>
    private (string Where, Dictionary<string, object> Ps) BuildPoolWhere(TaskExecutionStatus? status)
    {
        var where = $"{Extract(UsharpRecordPaths.ThreadId)}=@t";
        var ps = Params(("t", PoolThreadId));
        if (status is not null) { where += $" AND {Extract(UsharpRecordPaths.Status)}=@s"; ps["@s"] = status.Value.ToString(); }
        return (where, ps);
    }

    private string? ReadDocText(string taskKey)
    {
        var rows = Query(KvUsharpTable.SelectByKeySql(Table), Params(("k", taskKey)));
        return rows.Count == 0 ? null : SqliteRows.Nullable(rows[0], "Doc");
    }

    private bool TryReadItem(string doc, out TItem? item)
    {
        item = default;
        return !string.IsNullOrWhiteSpace(doc) && _codec.TryDeserializeRecord<TItem>(doc, out item) && item is not null;
    }

    private (string? Status, string? CreatedAt) ReadEnvelope(string taskKey)
    {
        var text = ReadDocText(taskKey);
        if (text is null || !_codec.TryDeserializeRecord(text, out _, out _, out _, out var body)) return (null, null);
        return (Str(body, FieldStatus), Str(body, FieldCreatedAt));
    }

    private static string? Str(UsharpRecord body, string name)
        => body.TryGet(name, out var v) && v is UsharpScalar sc && sc.Kind != UsharpScalarKind.Null ? sc.Text : null;
}
