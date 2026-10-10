using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
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
        _codec = codec ?? JsonDocumentCodec.Instance;
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

        var doc = BuildBody(node, createdAt, now);
        var text = _codec.SerializeRecord(RecordId(node.NodeKey), TableId, node.NodeKey, doc);
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

    /// <summary>批量删除（去重）：先查命中键再单条 <c>DELETE ... IN</c> 批量执行（分块防参数上限），
    /// 返回实际删除键（按入参顺序）。修复旧版逐条删除的 N+1 往返与 async-blocking，循环内检查取消令牌。</summary>
    public Task<IReadOnlyList<string>> DeleteManyAsync(IEnumerable<string> nodeKeys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keys = (nodeKeys ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        var removed = new List<string>(keys.Count);
        const int chunk = 500;
        for (var i = 0; i < keys.Count; i += chunk)
        {
            ct.ThrowIfCancellationRequested();
            var slice = keys.Skip(i).Take(chunk).ToList();
            var existingSet = Query(
                    $"SELECT {KvDocumentTable.ColNodeKey} FROM [{Table}] WHERE {KvDocumentTable.ColNodeKey} IN ({InParams(slice.Count)})",
                    BuildInParams(slice))
                .Select(r => SqliteRows.Str(r, "NodeKey")) // 行字典键为 reader.GetName()（不带方括号），与 ColNodeKey(带方括号) 不同，勿误用
                .Where(nk => !string.IsNullOrWhiteSpace(nk))
                .ToHashSet(StringComparer.Ordinal);
            var toDelete = slice.Where(existingSet.Contains).ToList();
            if (toDelete.Count == 0) continue;
            ExecuteNonQuery(
                $"DELETE FROM [{Table}] WHERE {KvJsonTable.ColNodeKey} IN ({InParams(toDelete.Count)})",
                BuildInParams(toDelete));
            removed.AddRange(toDelete);
        }
        return Task.FromResult<IReadOnlyList<string>>(removed);
    }

    private static string InParams(int count) => string.Join(",", Enumerable.Range(0, count).Select(j => $"@k{j}"));

    private static Dictionary<string, object> BuildInParams(IReadOnlyList<string> keys)
    {
        var ps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < keys.Count; j++) ps[$"@k{j}"] = keys[j];
        return ps;
    }

    /// <summary>构造节点记录体（字段序 = 规范形键序；null/空白字段不写出）。
    /// Id 自增列、NodeKey 唯一列不入记录体；载荷为**中立 <see cref="UsharpValue"/>**（数据内核原生表示）。</summary>
    private static UsharpRecord BuildBody(NodeRecord node, string createdAt, string updatedAt)
    {
        var fields = new List<KeyValuePair<string, UsharpValue>>(10);
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.Add(new(name, UsharpScalar.Str(value)));
        }

        Add("nodeType", string.IsNullOrWhiteSpace(node.NodeType) ? NodeTypes.Node : node.NodeType);
        Add("threadId", node.ThreadId);
        Add("key", node.Key);
        Add("status", node.Status);
        Add("stepKey", node.StepKey);
        Add("decision", node.Decision);
        if (node.Payload is not null) fields.Add(new("payload", node.Payload));
        if (node.Relations is not null) fields.Add(new("relations", node.Relations));
        Add("createdAt", createdAt);
        Add("updatedAt", updatedAt);
        return new UsharpRecord(fields);
    }

    /// <summary>行 → 节点记录：读库即解析一次记录实例，字段经投影落到运行态 <see cref="NodeRecord"/>。</summary>
    private NodeRecord? FromRow(IReadOnlyDictionary<string, object> row)
    {
        var nodeKey = SqliteRows.Str(row, "NodeKey");
        if (string.IsNullOrWhiteSpace(nodeKey)) return null;

        _codec.TryDeserializeRecord(SqliteRows.Str(row, "Doc"), out _, out _, out _, out var body);
        string? S(string name) => body.TryGet(name, out var v) && v is UsharpScalar sc && sc.Kind != UsharpScalarKind.Null ? sc.Text : null;
        UsharpValue? V(string name) => body.TryGet(name, out var v) && v is not UsharpScalar { Kind: UsharpScalarKind.Null } ? v : null;

        return new NodeRecord
        {
            Id = SqliteRows.Long(row, "Id"),
            NodeKey = nodeKey,
            NodeType = S("nodeType") ?? NodeTypes.Node,
            ThreadId = S("threadId"),
            Key = S("key"),
            Status = S("status"),
            StepKey = S("stepKey"),
            Decision = S("decision"),
            Payload = V("payload"),
            Relations = V("relations"),
            CreatedAt = S("createdAt"),
            UpdatedAt = S("updatedAt"),
        };
    }
}

