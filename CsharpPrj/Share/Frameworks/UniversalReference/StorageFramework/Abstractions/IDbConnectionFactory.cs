namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>数据库连接工厂端口。收敛全局静态连接串/路径为实例化注入。</summary>
public interface IDbConnectionFactory
{
    string ConnectionString { get; }
}
