using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>配置即数据行端口（ur_config，对齐架构设计 §八 / P3 数据驱动维护）：改数据行即改行为。</summary>
public interface IConfigStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
}
