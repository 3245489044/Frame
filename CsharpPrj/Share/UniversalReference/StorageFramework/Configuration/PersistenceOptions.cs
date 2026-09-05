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

    /// <summary>连接串。为空时由后端依据 <see cref="DatabasePath"/> 构造。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>表前缀隔离。</summary>
    public string TablePrefix { get; set; } = string.Empty;

    /// <summary>是否自动创建 Schema（建表）。</summary>
    public bool AutoCreateSchema { get; set; }
}
