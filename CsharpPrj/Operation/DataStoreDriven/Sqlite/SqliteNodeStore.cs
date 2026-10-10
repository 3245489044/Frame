using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// SQLite 基础节点库（三库收敛的①节点库，表 ur_nodes）——极简「键名 + 一个 JSON」。
/// 实现 <see cref="INodeStore"/>，承载全部逻辑/记录节点 —— input / output / event / checkpoint(记录点) /
/// config / keymapping / resource，以 <see cref="NodeRecord.NodeType"/> 判别、<see cref="NodeRecord.NodeKey"/> 唯一（UNIQUE）。
/// <para>
/// 存储形态 = 唯一事实在 <see cref="NodeDoc"/>（一个 JSON 文档）；NodeKey 为唯一键。全部节点元数据
/// （NodeType/ThreadId/Key/Status/PayloadJson/RelationsJson/CreatedAt/UpdatedAt）随 JSON 持久化（真单源、节点自包含）；
/// 表仅 Id/NodeKey/Json 三列，NodeType/ThreadId 过滤由 JSON 表达式索引（json_extract）支撑，扩展新字段只需向 JSON 加键，表结构零改动。
/// </para>
/// </summary>
public sealed class SqliteNodeStore : KvJsonStoreBase, INodeStore
{
    private readonly IDocumentCodec _codec;

    /// <summary>注入命令执行器 + 表名（前缀 + <see cref="TableNames.Nodes"/>）。executor 由基类包装为
    /// <see cref="AmbientTransactionAwareCommandExecutor"/> 以纳入骨干 SQLite 环境事务（单库原子性）。
    /// 编解码缺省 = JSON（零行为变更）；注入 <c>UsharpDocumentCodec</c> ⇒ 行载荷 USharp 化。</summary>
    public SqliteNodeStore(IDbCommandExecutor executor, string tableName, IDocumentCodec? codec = null)
        : base(executor, tableName)
    {
        _codec = codec ?? JsonDocumentCodec.Instance;
    }

    public string TableName => Table;

    /// <summary>幂等建表：纯「键值 + 一个 JSON」——唯一事实在 Json，NodeKey 唯一，Id 自增。
    /// 全部节点元数据随 Json 持久化；NodeType/ThreadId 过滤由 JSON 表达式索引支撑（不新增物理列）。
    /// 旧版散列表（含 CreatedAt/NodeType 等列）自动迁移收敛（<see cref="MigrateLegacySchema"/>）。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 数据即声明：真源列 [Doc]（USharp 文本）+ 派生索引列 [IndexJson]；旧 KvJson 表就地迁移。
        KvDocumentTable.EnsureCreated(Executor, Table);
        KvDocumentTable.MigrateFromKvJson(Executor, Table);
        // 旧版表迁移：散列/生成列折入 Json 并清理旧索引与列（仅旧库命中，幂等）。
        MigrateLegacySchema();
        // 过滤索引落在**派生索引列** [IndexJson]（真源 [Doc] 为 USharp 文本，不可直接 json_extract）。
        KvDocumentTable.EnsureIndexJsonIndex(Executor, Table, "idx_nodes_type", KvJsonTable.Path.NodeType, KvDocumentTable.ColId);
        KvDocumentTable.EnsureIndexJsonIndex(Executor, Table, "idx_nodes_thread", KvJsonTable.Path.ThreadId, KvDocumentTable.ColId);
        return Task.CompletedTask;
    }

    /// <summary>旧版 ur_nodes（含 CreatedAt/NodeType 等散列列）数据折入 Json 并清理旧索引/列；仅旧库命中，幂等。</summary>
    private void MigrateLegacySchema()
    {
        var cols = Query($"PRAGMA table_info([{Table}])");
        var names = cols.Select(r => SqliteRows.Str(r, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("CreatedAt") && !names.Contains("NodeType"))
            return; // 已是纯键值表，无需迁移。
        // ① 把散列时间折入 Json（NodeType/ThreadId 本就是 Json 派生，无需折入）。
        ExecuteNonQuery(
            $"UPDATE [{Table}] SET {KvJsonTable.ColJson} = json_set({KvJsonTable.ColJson}, {KvJsonTable.PathRef(KvJsonTable.Path.CreatedAt)}, [CreatedAt], {KvJsonTable.PathRef(KvJsonTable.Path.UpdatedAt)}, [UpdatedAt]) " +
            $"WHERE json_extract({KvJsonTable.ColJson}, {KvJsonTable.PathRef(KvJsonTable.Path.CreatedAt)}) IS NULL AND [CreatedAt] IS NOT NULL;");
        // ② 清理旧索引（释放 idx_nodes_type/idx_nodes_thread 名，供新表达式索引复用）。
        ExecuteNonQuery("DROP INDEX IF EXISTS [idx_nodes_type];");
        ExecuteNonQuery("DROP INDEX IF EXISTS [idx_nodes_thread];");
        // ③ 清列（SQLite ≥3.35）：先删索引再删列，避免生成列/索引依赖冲突。
        ExecuteNonQuery("ALTER TABLE [" + Table + "] DROP COLUMN [NodeType];");
        ExecuteNonQuery("ALTER TABLE [" + Table + "] DROP COLUMN [ThreadId];");
        ExecuteNonQuery("ALTER TABLE [" + Table + "] DROP COLUMN [CreatedAt];");
        ExecuteNonQuery("ALTER TABLE [" + Table + "] DROP COLUMN [UpdatedAt];");
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

        var doc = BuildDoc(node, createdAt, now);
        var text = _codec.Serialize(doc);
        // 派生索引列：仅供 json_extract 索引（真源 = [Doc]，见 KvDocumentTable 头注）。
        var indexJson = JsonSerializer.Serialize(doc, JsonDocumentCodec.JsonOptions);
        ExecuteNonQuery(KvDocumentTable.UpsertSql(Table),
            Params(("k", node.NodeKey), ("d", text), ("ix", indexJson)));

        var rows = Query(KvDocumentTable.SelectByKeySql(Table, KvDocumentTable.ColId), Params(("k", node.NodeKey)));
        var id = rows.Count > 0 ? SqliteRows.Long(rows[0], "Id") : 0L;
        return id;
    }

    public Task<NodeRecord?> GetAsync(string nodeKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var rows = Query(KvDocumentTable.SelectByKeySql(Table, $"[Id],{KvDocumentTable.ColNodeKey},{KvDocumentTable.ColDoc}"), Params(("k", nodeKey)));
        return Task.FromResult(rows.Count > 0 ? FromRow(rows[0]) : null);
    }

    /// <summary>分页列出节点：NodeType/ThreadId 经 JSON 表达式索引（json_extract）过滤，afterId 增量分页。</summary>
    public Task<IReadOnlyList<NodeRecord>> ListAsync(string? nodeType = null, string? threadId = null,
        long? afterId = null, int limit = 100, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var where = new List<string>();
        var ps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (nodeType is not null) { where.Add($"{IndexExtract(KvJsonTable.Path.NodeType)}=@nt"); ps["@nt"] = nodeType; }
        if (threadId is not null) { where.Add($"{IndexExtract(KvJsonTable.Path.ThreadId)}=@tid"); ps["@tid"] = threadId; }
        if (afterId is not null) { where.Add("[Id]>@aid"); ps["@aid"] = afterId.Value; }

        var sql = $"SELECT [Id],{KvDocumentTable.ColNodeKey},{KvDocumentTable.ColDoc} FROM [{Table}]";
        if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
        sql += " ORDER BY [Id] LIMIT @lim";
        ps["@lim"] = Math.Max(0, limit);

        var rows = Query(sql, ps);
        return Task.FromResult<IReadOnlyList<NodeRecord>>(rows.Select(FromRow).Where(r => r is not null).Select(r => r!).ToList());
    }

    public Task<bool> DeleteAsync(string nodeKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var removed = ExecuteNonQuery(KvDocumentTable.DeleteByKeySql(Table), Params(("k", nodeKey)));
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

    /// <summary>派生索引列 [IndexJson] 上的 json_extract 表达式（与建索引时逐字一致）。</summary>
    private static string IndexExtract(string jsonPath)
        => $"json_extract({KvDocumentTable.ColIndexJson},'{jsonPath}')";

    private static Dictionary<string, object> BuildInParams(IReadOnlyList<string> keys)
    {
        var ps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < keys.Count; j++) ps[$"@k{j}"] = keys[j];
        return ps;
    }

    /// <summary>构造节点文档（NodeType/ThreadId/Key/Status/PayloadJson/RelationsJson/CreatedAt/UpdatedAt）；
    /// 文本序列化由 <see cref="IDocumentCodec"/> 承担（真单源）；Id 自增列、NodeKey 唯一列不入文档。</summary>
    private static NodeDoc BuildDoc(NodeRecord node, string createdAt, string updatedAt) => new NodeDoc
    {
        NodeType = string.IsNullOrWhiteSpace(node.NodeType) ? NodeTypes.Node : node.NodeType,
        ThreadId = node.ThreadId,
        Key = node.Key,
        Status = node.Status,
        StepKey = node.StepKey,
        Decision = node.Decision,
        PayloadJson = node.PayloadJson ?? "{}",
        RelationsJson = node.RelationsJson,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
    };

    private NodeRecord? FromRow(IReadOnlyDictionary<string, object> row)
    {
        var nodeKey = SqliteRows.Str(row, "NodeKey");
        if (string.IsNullOrWhiteSpace(nodeKey)) return null;

        var doc = _codec.Deserialize<NodeDoc>(SqliteRows.Str(row, "Doc"));
        return new NodeRecord
        {
            Id = SqliteRows.Long(row, "Id"),
            NodeKey = nodeKey,
            NodeType = doc?.NodeType ?? NodeTypes.Node,
            ThreadId = doc?.ThreadId,
            Key = doc?.Key,
            Status = doc?.Status,
            StepKey = doc?.StepKey,
            Decision = doc?.Decision,
            PayloadJson = doc?.PayloadJson ?? "{}",
            RelationsJson = doc?.RelationsJson,
            CreatedAt = doc?.CreatedAt,
            UpdatedAt = doc?.UpdatedAt,
        };
    }

    /// <summary>节点 JSON 文档形态（键名 + 一个 JSON 的载荷部分）。</summary>
    private sealed class NodeDoc
    {
        public string? NodeType { get; set; }

        public string? ThreadId { get; set; }

        public string? Key { get; set; }

        public string? Status { get; set; }

        public string? StepKey { get; set; }

        public string? Decision { get; set; }

        public string? PayloadJson { get; set; }

        public string? RelationsJson { get; set; }

        public string? CreatedAt { get; set; }

        public string? UpdatedAt { get; set; }
    }
}

