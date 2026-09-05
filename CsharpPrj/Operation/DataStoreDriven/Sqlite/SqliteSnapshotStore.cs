using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>Sqlite 快照存储（ur_tasks 状态集合，与任务池同表双角色）。按 (ThreadId, SessionId) 幂等覆写最新快照，
/// 表收敛为纯「业务键（NodeKey）+ 一个 Json」：NodeKey = 快照键（sessionId，缺省回落 threadId），
/// ThreadId/SessionId 折入 Json（随快照负载持久化，不单独成列，与 ur_nodes 同构）。</summary>
public sealed class SqliteSnapshotStore : KvJsonStoreBase, ISnapshotStore
{
    public SqliteSnapshotStore(IDbCommandExecutor executor, string table)
        : base(executor, table)
    {
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        ct.ThrowIfCancellationRequested();
        // NodeKey = 快照键（sessionId，缺省回落 threadId）；ThreadId/SessionId 折入 Json（对象快照可注入，非对象保留原样）。
        var key = string.IsNullOrWhiteSpace(sessionId) ? threadId : sessionId;
        var json = InjectEnvelope(snapshotJson, threadId, sessionId);
        ExecuteNonQuery(KvJsonTable.UpsertSql(Table), Params(("k", key), ("j", json)));
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string sql;
        Dictionary<string, object> p;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            sql = KvJsonTable.SelectByKeySql(Table);
            p = Params(("k", sessionId));
        }
        else
        {
            sql = $"SELECT {KvJsonTable.ColJson} FROM [{Table}] WHERE {KvJsonTable.JsonExtract(KvJsonTable.Path.ThreadId)}=@t ORDER BY {KvJsonTable.ColId} DESC LIMIT 1";
            p = Params(("t", threadId));
        }

        var rows = Query(sql, p);
        if (rows.Count > 0 && rows[0].TryGetValue("Json", out var v) && v is not null && v is not DBNull)
        {
            var s = v.ToString();
            if (!string.IsNullOrWhiteSpace(s))
                return Task.FromResult<string?>(s);
        }
        return Task.FromResult<string?>(null); // B3：表内 NULL 或空快照 → null（「无快照」语义），勿返回空串失真
    }

    /// <summary>把 ThreadId/SessionId 注入对象快照 Json（供无 sessionId 时按 ThreadId 检索）；空串/非对象（异常载荷）原样返回。</summary>
    private static string InjectEnvelope(string json, string threadId, string? sessionId)
    {
        // B3 空快照守卫：空/空白原样保留（Load 侧按空串判 null），避免 JsonNode.Parse("") 抛异常。
        if (string.IsNullOrWhiteSpace(json)) return json;
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj) return json;
        obj["ThreadId"] = threadId;
        obj["SessionId"] = sessionId ?? string.Empty;
        return obj.ToJsonString();
    }
}