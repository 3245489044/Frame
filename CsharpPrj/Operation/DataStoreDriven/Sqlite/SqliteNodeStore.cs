using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Batch;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// SQLite 基础节点库（三库收敛的①节点库，表 ur_nodes）——**纯 USharp 真源**。
/// 实现 <see cref="INodeStore"/>，承载全部逻辑/记录节点 —— input / output / event / checkpoint(记录点) /
/// config / keymapping / resource，以 <see cref="NodeRecord.NodeType"/> 判别、<see cref="NodeRecord.NodeKey"/> 唯一（UNIQUE）。
/// <para>
/// 存储形态 = 唯一事实在 <c>[Doc]</c>（USharp 文本，经 <see cref="IDocumentCodec"/>）；表为**纯三列**
/// <c>[Id]/[NodeKey]/[Doc]</c>，**无兼容派生列**。NodeType/ThreadId 过滤由存储**在内存解码 USharp 后**完成
/// （USharp 文本无法 SQL 索引），故 <see cref="ListAsync"/> 为扫描式过滤。
/// </para>
/// </summary>
public sealed class SqliteNodeStore : KvJsonStoreBase, INodeStore
{
    private readonly IDocumentCodec _codec;
    private readonly ITableSchemaSource? _schemaSource;

    /// <summary>记录头 table 标识（<c>[Record] node:&lt;key&gt;: table=Nodes | key=&lt;key&gt;</c>）。</summary>
    private const string TableId = "Nodes";

    private static string RecordId(string nodeKey) => "node:" + nodeKey;

    /// <summary>注入命令执行器 + 表名（前缀 + <see cref="TableNames.Nodes"/>）。executor 由基类包装为
    /// <see cref="AmbientTransactionAwareCommandExecutor"/> 以纳入骨干 SQLite 环境事务（单库原子性）。
    /// 编解码缺省 = JSON（零行为变更）；注入 <c>UsharpDocumentCodec</c> ⇒ 行载荷 USharp 化。
    /// <paramref name="schemaSource"/> 缺省 ⇒ 建表回落手写 <see cref="KvUsharpTable.SchemaColumns"/>。</summary>
    public SqliteNodeStore(IDbCommandExecutor executor, string tableName, IDocumentCodec? codec = null, ITableSchemaSource? schemaSource = null)
        : base(executor, tableName)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec), "ur_nodes 记录型存储须注入 IDocumentCodec（USharp codec）。");
        _schemaSource = schemaSource;
    }

    public string TableName => Table;

    /// <summary>幂等建表：纯三列 <c>[Id]/[NodeKey]/[Doc]</c>（无兼容派生列）。
    /// 表结构声明源（USharp [Table] tables.ushar，container=nodes）优先；缺件回落手写 <see cref="KvUsharpTable.SchemaColumns"/>。
    /// 旧表（含 <c>[IndexJson]</c> 兼容列或旧 <c>[Json]</c> 列）经 <see cref="KvUsharpTable.MigrateToPure"/> 收敛为纯三列。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var declared = _schemaSource?.GetColumns(TableNames.Nodes) ?? _schemaSource?.GetColumns("nodes");
        KvUsharpTable.EnsureCreated(Executor, Table, declared);
        KvUsharpTable.MigrateToPure(Executor, Table);
        return Task.CompletedTask;
    }

    /// <summary>NodeKey 幂等 upsert（<c>ON CONFLICT(NodeKey) DO UPDATE</c> 覆写 Json/UpdatedAt，首次 CreatedAt 保留），返回该 NodeKey 对应 Id。
    /// 时间戳随 Json 持久化：CreatedAt=首次写入（既有行保留），UpdatedAt=本次写入。
    /// 注：SQLite 的 <c>last_insert_rowid()</c> 在 ON CONFLICT DO UPDATE（更新路径）下返回 0，故不能依赖它取被更新行 Id，
    /// 改为 upsert 后按 NodeKey 回查 Id（幂等，新旧路径均返回同一 Id）。</summary>
    public async Task<long> PutAsync(NodeRecord node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ct.ThrowIfCancellationRequested();
        var now = PersistenceHelper.NowIso();
        var createdAt = node.CreatedAt;
        if (string.IsNullOrWhiteSpace(createdAt))
        {
            var existing = await GetAsync(node.NodeKey, ct).ConfigureAwait(false);
            createdAt = existing?.CreatedAt;
        }
        createdAt = string.IsNullOrWhiteSpace(createdAt) ? now : createdAt;
        // U-7 薄视图：时间戳直写同一 UsharpRecord 实例（CreatedAt 首次保留 / UpdatedAt 本次）。
        node.CreatedAt = createdAt;
        node.UpdatedAt = now;

        var text = _codec.SerializeRecord(RecordId(node.NodeKey), TableId, node.NodeKey, node.Record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table),
            Params(("k", node.NodeKey), ("d", text)));

        var rows = Query(KvUsharpTable.SelectByKeySql(Table, KvUsharpTable.ColId), Params(("k", node.NodeKey)));
        var id = rows.Count > 0 ? SqliteRows.Long(rows[0], "Id") : 0L;
        return id;
    }

    public Task<NodeRecord?> GetAsync(string nodeKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(KvUsharpTable.SelectByKeySql(Table, $"[Id],{KvUsharpTable.ColNodeKey},{KvUsharpTable.ColDoc}"), Params(("k", nodeKey)));
        return Task.FromResult(rows.Count > 0 ? FromRow(rows[0]) : null);
    }

    /// <summary>
    /// 分页列出节点：**无兼容派生列** ⇒ 过滤无法下推 SQL，改为按 Id 游标**分块扫描 + 内存解码 USharp 后过滤**
    /// （NodeType/ThreadId），累积到 <paramref name="limit"/> 即止。afterId 增量分页语义与旧实现一致。
    /// </summary>
    public Task<IReadOnlyList<NodeRecord>> ListAsync(string? nodeType = null, string? threadId = null,
        long? afterId = null, int limit = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var take = Math.Max(0, limit);
        var results = new List<NodeRecord>(Math.Min(take, 256));
        if (take == 0) return Task.FromResult<IReadOnlyList<NodeRecord>>(results);

        var cursor = afterId ?? 0L;
        const int chunk = 512;
        while (results.Count < take)
        {
            ct.ThrowIfCancellationRequested();
            var rows = Query(
                $"SELECT [Id],{KvUsharpTable.ColNodeKey},{KvUsharpTable.ColDoc} FROM [{Table}] WHERE {KvUsharpTable.ColId}>@c ORDER BY {KvUsharpTable.ColId} LIMIT @n",
                Params(("c", cursor), ("n", chunk)));
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                cursor = SqliteRows.Long(row, "Id");
                var rec = FromRow(row);
                if (rec is null) continue;
                if (nodeType is not null && !string.Equals(rec.NodeType, nodeType, StringComparison.Ordinal)) continue;
                if (threadId is not null && !string.Equals(rec.ThreadId, threadId, StringComparison.Ordinal)) continue;
                results.Add(rec);
                if (results.Count >= take) break;
            }

            if (rows.Count < chunk) break;
        }
        return Task.FromResult<IReadOnlyList<NodeRecord>>(results);
    }

    public Task<bool> DeleteAsync(string nodeKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var removed = ExecuteNonQuery(KvUsharpTable.DeleteByKeySql(Table), Params(("k", nodeKey)));
        return Task.FromResult(removed > 0);
    }

    /// <summary>批量删除（去重）：**薄适配通用批量端口 <see cref="IBatchStore"/>**（C10）——批删唯一实现收敛到
    /// <see cref="SqliteBatchStore"/>（经同一环境事务感知执行器 = 库级单写通道），本处不再自拼 <c>DELETE ... IN</c>。
    /// 返回实际删除键（按入参顺序）；不存在键 <see cref="BatchItemStatus.NotFound"/> 不计入，循环内检查取消令牌。</summary>
    public Task<IReadOnlyList<string>> DeleteManyAsync(IEnumerable<string> nodeKeys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keys = (nodeKeys ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        // 键列名不加方括号（SqliteBatchStore 内部补 []）；表名走基类解析后的纯净名。
        var result = new SqliteBatchStore(Executor)
            .Delete(new BatchDeleteRequest(Table, keys, "NodeKey"));

        var removed = new List<string>(keys.Count);
        foreach (var item in result.Items)
        {
            if (item.Status == BatchItemStatus.Ok)
            {
                removed.Add(item.Key);
            }
        }
        return Task.FromResult<IReadOnlyList<string>>(removed);
    }

    /// <summary>行 → 节点记录：读库即解析一次记录实例，运行态 <see cref="NodeRecord"/> 为**薄视图**
    /// （直接持有 <see cref="UsharpRecord"/>，字段经 <c>TryGet</c>/<c>Set</c> 直读写同一实例，非逐字段拷贝投影）。</summary>
    private NodeRecord? FromRow(IReadOnlyDictionary<string, object> row)
    {
        var nodeKey = SqliteRows.Str(row, "NodeKey");
        if (string.IsNullOrWhiteSpace(nodeKey)) return null;

        _codec.TryDeserializeRecord(SqliteRows.Str(row, "Doc"), out _, out _, out _, out var body);

        return new NodeRecord(body)
        {
            Id = SqliteRows.Long(row, "Id"),
            NodeKey = nodeKey,
        };
    }
}

