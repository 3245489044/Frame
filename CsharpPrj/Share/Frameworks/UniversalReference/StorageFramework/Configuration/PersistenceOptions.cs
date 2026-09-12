using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.StorageFramework.Configuration;

/// <summary>
/// 持久化配置载体（纯数据，不内置业务默认值）。默认值唯一权威 = Agent.Launcher.AgentConfig
/// （组合根统一填充后经 PersistenceProviderFactory 注入各后端）；配置绑定见 <see cref="PersistenceConfigBinder"/>。
/// </summary>
public sealed class PersistenceOptions
{
    public PersistenceProvider Provider { get; set; }

    /// <summary>数据库文件路径（收敛原 SqliteRelate.DataBasePath / LocalStorage 默认路径）。</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// 可写状态根（目录）—— 数据库 / 图库 / 运行时 / 凭据 / 日志的共同父目录。
    /// 配置声明此字段即成为落点**唯一入口**（派生出 <see cref="DatabasePath"/> 等，无需逐个改路径）；
    /// 未声明时由组合根按「注入 catalog → ROUTE_STATE_ROOT → 兜底」解析。
    /// </summary>
    public string? StateRoot { get; set; }

    /// <summary>骨干数据库文件名（缺省 agent.db；仅状态根派生时生效，为 null 用状态层默认）。</summary>
    public string? DatabaseFileName { get; set; }

    /// <summary>关系/引用图库文件名（缺省 graph.db；仅状态根派生时生效，为 null 用状态层默认）。</summary>
    public string? GraphDatabaseFileName { get; set; }

    /// <summary>连接串。为空时由后端依据 <see cref="DatabasePath"/> 构造。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>表前缀隔离。</summary>
    public string TablePrefix { get; set; } = string.Empty;

    /// <summary>是否自动创建 Schema（建表）。</summary>
    public bool AutoCreateSchema { get; set; }
}
