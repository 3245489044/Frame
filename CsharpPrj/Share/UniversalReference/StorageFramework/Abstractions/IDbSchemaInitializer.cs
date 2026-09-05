using System.Threading;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>Schema 初始化端口。各后端负责创建（幂等）自身表结构（ur_* 等）。</summary>
public interface IDbSchemaInitializer
{
    Task InitializeAsync(CancellationToken ct = default);
}
