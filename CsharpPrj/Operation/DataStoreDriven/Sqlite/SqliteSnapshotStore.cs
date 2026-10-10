using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Plate.DslMethod.Usharp;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;

namespace Operation.DataStoreDriven.Sqlite;

/// <summary>
/// Sqlite 快照存储（ur_tasks 状态集合，与任务池同表双角色）。按 (ThreadId, SessionId) 幂等覆写最新快照。
/// <para>
/// 表为**纯 USharp 三列**；真源 <c>[Doc]</c> = <c>[Record] snapshot:&lt;key&gt;: table=Tasks | key=&lt;key&gt;</c> 实例规范文本
/// （ThreadId/SessionId/Payload 记录字段）；无 <c>[IndexJson]</c>。NodeKey = 快照键（sessionId，缺省回落 threadId）。
/// ThreadId 检索经 <c>usharp_get([Doc],'$.ThreadId')</c> 表达式索引在 SQL 侧下推。
/// </para>
/// </summary>
public sealed class SqliteSnapshotStore : KvJsonStoreBase, ISnapshotStore
{
    private const string TableId = "Tasks";
    private const string FieldThreadId = "ThreadId";
    private const string FieldSessionId = "SessionId";
    private const string FieldPayload = "Payload";

    private readonly IDocumentCodec _codec;

    public SqliteSnapshotStore(IDbCommandExecutor executor, string table, IDocumentCodec? codec = null)
        : base(executor, table)
    {
        _codec = codec ?? JsonDocumentCodec.Instance;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        KvUsharpTable.ResetToPureIfNeeded(Executor, Table);
        KvUsharpTable.EnsureCreated(Executor, Table);
        KvUsharpTable.EnsureUsharpIndex(Executor, Table, "idx_tasks_threadid", KvJsonTable.Path.ThreadId);
        return Task.CompletedTask;
    }

    public Task SaveAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        ct.ThrowIfCancellationRequested();
        // NodeKey = 快照键（sessionId，缺省回落 threadId）。
        var key = string.IsNullOrWhiteSpace(sessionId) ? threadId : sessionId;
        // B3 空快照守卫：空/空白原样存空文本（Load 侧按空判 null），不构造文档对象。
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", key), ("d", "")));
            return Task.CompletedTask;
        }
        var record = BuildRecord(threadId, sessionId, snapshotJson);
        var text = _codec.SerializeRecord(RecordId(key), TableId, key, record);
        ExecuteNonQuery(KvUsharpTable.UpsertSql(Table), Params(("k", key), ("d", text)));
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string threadId, string? sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string sql;
        Dictionary<string, object> p;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            sql = KvUsharpTable.SelectByKeySql(Table);
            p = Params(("k", sessionId));
        }
        else
        {
            sql = $"SELECT {KvUsharpTable.ColDoc} FROM [{Table}] WHERE {KvUsharpTable.UsharpExtract(KvJsonTable.Path.ThreadId)}=@t " +
                  $"ORDER BY {KvUsharpTable.ColId} DESC LIMIT 1";
            p = Params(("t", threadId));
        }

        var rows = Query(sql, p);
        if (rows.Count == 0) return Task.FromResult<string?>(null);
        var doc = SqliteRows.Str(rows[0], "Doc");
        if (string.IsNullOrWhiteSpace(doc))
            return Task.FromResult<string?>(null); // B3：空快照 → null（「无快照」语义），勿返回空串失真
        // 对外保持 JSON 契约：对象载荷平铺（含 envelope）⇒ 整记录渲染；非对象载荷 ⇒ Payload 字段。
        if (!_codec.TryDeserializeRecord(doc, out _, out _, out _, out var body)) return Task.FromResult<string?>(doc);
        if (body.TryGet(FieldPayload, out var payload)) return Task.FromResult<string?>(UsharpValueJson.ToJson(payload));
        return Task.FromResult<string?>(UsharpValueJson.ToJson(body));
    }

    private static string RecordId(string key) => "snapshot:" + key;

    /// <summary>构造快照记录：对象载荷 ⇒ 平铺合并（+ ThreadId/SessionId envelope，供按 ThreadId 检索）；
    /// 非对象载荷 ⇒ 包为 Payload 字段（保留原值）。</summary>
    private static UsharpRecord BuildRecord(string threadId, string? sessionId, string snapshotJson)
    {
        var payload = ParsePayload(snapshotJson);
        if (payload is UsharpRecord obj)
        {
            var fields = new List<KeyValuePair<string, UsharpValue>>(obj.Fields.Count + 2);
            foreach (var f in obj.Fields)
                if (!string.Equals(f.Key, FieldThreadId, StringComparison.Ordinal) &&
                    !string.Equals(f.Key, FieldSessionId, StringComparison.Ordinal))
                    fields.Add(f);
            fields.Add(new(FieldThreadId, UsharpScalar.Str(threadId)));
            fields.Add(new(FieldSessionId, UsharpScalar.Str(sessionId ?? string.Empty)));
            return new UsharpRecord(fields);
        }
        return new UsharpRecord(new[]
        {
            new KeyValuePair<string, UsharpValue>(FieldThreadId, UsharpScalar.Str(threadId)),
            new KeyValuePair<string, UsharpValue>(FieldSessionId, UsharpScalar.Str(sessionId ?? string.Empty)),
            new KeyValuePair<string, UsharpValue>(FieldPayload, payload),
        });
    }

    /// <summary>快照载荷文本 → 中立值（非法 JSON ⇒ 不透明字符串标量，原样保留）。</summary>
    private static UsharpValue ParsePayload(string snapshotJson)
    {
        try { return UsharpValueJson.Parse(snapshotJson); }
        catch { return UsharpScalar.Str(snapshotJson); }
    }
}
