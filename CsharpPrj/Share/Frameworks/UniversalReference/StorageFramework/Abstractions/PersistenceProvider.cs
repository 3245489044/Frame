namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>
/// 持久化后端类型。由配置驱动（<c>Persistence.Provider</c>），对应 <c>PersistenceProviderFactory</c> 的注册表键。
/// </summary>
public enum PersistenceProvider
{
    Sqlite,
    LiteDB,
    Graph,
    Local,
}
