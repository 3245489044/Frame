using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>内存配置即数据行（ur_config）占位实现：非 Sqlite 后端 / 测试使用；进程重启即失。</summary>
public sealed class InMemoryConfigStore : IConfigStore
{
    private readonly Dictionary<string, string> _map = new();
    private readonly object _lock = new();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<string?>(_map.TryGetValue(key, out var v) ? v : null);
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        lock (_lock) _map[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        lock (_lock) _map.Remove(key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_map));
    }
}
