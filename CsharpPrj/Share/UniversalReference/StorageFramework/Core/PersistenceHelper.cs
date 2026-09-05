using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.StorageFramework.Core;

/// <summary>
/// 持久化层共享小工具：参数构造 / 元数据解析 / 连接串与路径解析。
/// 收敛各后端（Sqlite/LiteDB/Validator/Binder）重复手写的同构逻辑（SingleImplementation）。
/// </summary>
public static class PersistenceHelper
{
    /// <summary>共享 JSON 序列化选项：不写 null（收敛 Sqlite/LiteDB 两个后端重复声明的 JsonOptions）。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // 可读性：中文/特殊字符原样落盘（默认编码器把中文转 \uXXXX）；消费方以同一选项序列化，读取一致。
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>UTC ISO-8601 时间戳（收敛多后端重复的 <c>DateTime.UtcNow.ToString("o")</c>）。</summary>
    public static string NowIso() => DateTime.UtcNow.ToString("o");

    /// <summary>按 SQLite 参数键约定（含 <c>@</c>/<c>:</c> 前缀）构造参数字典（OrdinalIgnoreCase）。</summary>
    public static Dictionary<string, object> CreateParameters(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) dict[k] = v;
        return dict;
    }

    /// <summary>复合键拼接（checkpoint 的 ThreadId|Ns|Id、快照的 ThreadId|SessionId 统一单一来源，收敛多后端重复实现）。</summary>
    public static string CompositeKey(params string[] parts)
        => string.Join("|", parts);

    /// <summary>checkpoint 复合键（LangGraph 兼容；<see cref="ICheckpointStore"/> 各后端共用）。</summary>
    public static string CheckpointKey(string threadId, string? checkpointNs, string checkpointId)
        => $"{threadId}|{checkpointNs ?? string.Empty}|{checkpointId}";

    /// <summary>快照复合键（<see cref="ISnapshotStore"/> 各后端共用）。</summary>
    public static string SnapshotKey(string threadId, string? sessionId)
        => $"{threadId}|{sessionId ?? string.Empty}";

    /// <summary>checkpoint id 解析：空/空白 → 生成新 Guid（N 格式）；否则原样返回（收敛三后端重复的 id 生成）。</summary>
    public static string ResolveCheckpointId(string? checkpointId)
        => string.IsNullOrWhiteSpace(checkpointId) ? Guid.NewGuid().ToString("N") : checkpointId;

    /// <summary>把 JSON 反序列化为元数据字典；空/非法输入返回空字典。</summary>
    public static Dictionary<string, object> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object>();
        try { return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(); }
        catch { return new Dictionary<string, object>(); }
    }

    /// <summary>
    /// 引用键归一：Id 为空（含空串，<see cref="ResourceIdentifier"/> 默认 Id=""）时回退到资源唯一键
    /// <see cref="ResourceIdentifier.GetUniqueKey"/>，保证引用键与资源键一致，避免"空串 id"导致的引用写入/查询落空。
    /// 收敛 Sqlite/LiteDB 资源引用存储重复的 RefKey 实现（SingleImplementation）。
    /// </summary>
    public static string ResourceRefKey(ResourceIdentifier r)
        => string.IsNullOrEmpty(r.Id) ? r.GetUniqueKey() : r.Id;

    /// <summary>表前缀归一：空值即未配置（默认由 AgentConfig 统一提供），fail-fast 而非静默回退 <c>ur_</c>。</summary>
    public static string ResolveTablePrefix(string? tablePrefix)
    {
        if (string.IsNullOrWhiteSpace(tablePrefix))
            throw new InvalidOperationException("[Persistence] TablePrefix 未配置（默认由 AgentConfig 统一提供）。");
        return tablePrefix;
    }

    /// <summary>统一文件后端连接串构造（Sqlite/LiteDB 单一路径）：显式连接串优先，否则由 DatabasePath 构造。</summary>
    public static string BuildConnectionString(PersistenceProvider provider, PersistenceOptions options, string? basePath)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            throw new InvalidOperationException($"[Persistence] {provider} 后端必须配置 ConnectionString 或 DatabasePath。");

        var path = ResolveDatabasePath(options.DatabasePath, basePath)!;
        return provider switch
        {
            // Default Timeout=5000：System.Data.SQLite 连接串默认 busy timeout（等效 PRAGMA busy_timeout=5000），
            // 使非事务路径（SqliteSession）的写等待有界，杜绝并发写同一 agent.db 时无限阻塞（对齐 SqliteTransaction/DataUniversal 的 5s 兜底）。
            PersistenceProvider.Sqlite => $"Data Source={path};Version=3;Default Timeout=5000;",
            PersistenceProvider.LiteDB => $"Filename={path};",
            _ => throw new NotSupportedException($"[Persistence] {provider} 不是文件连接串后端。"),
        };
    }

    /// <summary>幂等建表 SQL（CREATE TABLE IF NOT EXISTS）。收敛各存储重复的 CreateSql。</summary>
    public static string CreateTableSql(string tableName, string[] columns)
        => $"CREATE TABLE IF NOT EXISTS [{tableName}] ({string.Join(", ", columns)});";

    /// <summary>数据库路径解析：绝对路径原样返回；相对路径与 basePath 组合；空返回 null。</summary>
    public static string? ResolveDatabasePath(string? databasePath, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) return null;
        return Path.IsPathRooted(databasePath)
            ? databasePath
            : string.IsNullOrWhiteSpace(basePath) ? databasePath : Path.Combine(basePath, databasePath);
    }

    /// <summary>确保数据库文件所在目录存在（Sqlite/LiteDB/Local 各后端收敛重复的建目录逻辑）；空路径直接返回。</summary>
    public static string? EnsureDirectoryFor(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) return null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>
/// 后端表/集合名后缀单一权威（Sqlite 表名与 LiteDB 集合名共用；收敛多处魔法字符串拼接）。
/// 消费方：<see cref="Sqlite.SqliteSchema"/>、各 Sqlite/LiteDB Store、门面装配。
/// 【三库收敛】物理落点收敛为三个逻辑存储：nodes（基础节点库，SQLite ur_nodes 表）、tasks（状态集合，SQL ur_tasks）、resources（关系库，LiteDB 图库集合）。
/// config/keymapping/checkpoint/snapshot 语义并入 nodes；references 并入 resources。旧后缀保留仅供迁移/兼容（勿新建消费者）。
/// </summary>
public static class TableNames
{
    /// <summary>① 基础节点库（SQLite ur_nodes 表：纯「键名 + 一个 JSON」，全元数据随 Json；承载 input/output/event/checkpoint/config/keymapping/resource）。</summary>
    public const string Nodes = "nodes";

    /// <summary>② 状态集合（SQL ur_tasks：任务定义 + 运行时状态 + 调用的模型）。</summary>
    public const string Tasks = "tasks";

    /// <summary>③ 关系库（LiteDB 图库集合 ur_resources/ur_references：连接 tasks ↔ nodes 的有向边，真源为 graph-log.db）。</summary>
    public const string Resources = "resources";

    /// <summary>④ 端点符号落盘缓存（LiteDB 图库集合 ur_endpoints：端点 Symbol/Metadata 持久化，可重建；权威仍在 SQL）。</summary>
    public const string Endpoints = "endpoints";

    /// <summary>⑤ 会话历史容器（SQL ur_histories：History = Task 更上一层的 class 容器（多 task 多 nodes），
    /// 表结构镜像 ur_tasks 纯「业务键 + 一个 SnapshotJson」；实体真源在 SQL，LiteDB 只存关系投影）。</summary>
    public const string Histories = "histories";

    // ===== 已并入三库的旧后缀（仅供迁移/兼容，勿新建消费者；逐步收敛到 nodes/tasks/resources） =====
    // 语义去向: KeyMappings→ur_nodes(keymapping) · References→ur_resources · Checkpoints→ur_nodes(checkpoint)
    //          Snapshots→ur_tasks(任务=状态根) · Config→ur_nodes(config)
    public const string KeyMappings = "keymappings";

    public const string References = "references";

    public const string Checkpoints = "checkpoints";

    public const string Snapshots = "snapshots";

    public const string Config = "config";
}
