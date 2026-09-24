using System.Security.Cryptography;
using System.Text;

namespace Modules.Node.Carriers;

/// <summary>载体覆写语义（U2：默认 append-only 只增不丢）。</summary>
public enum CarrierSemantics
{
    /// <summary>同位置异载荷不可覆盖：返回 Conflict（N5）。</summary>
    AppendOnly,

    /// <summary>同位置异载荷覆盖（last-write-wins；仅显式声明时使用）。</summary>
    LastWriteWins,
}

/// <summary>
/// 内存载体（测试/开发用；也是 N4/N5/N10 合同测试的基准实现）。
/// </summary>
public sealed class InMemoryNodeCarrier : INodeCarrier
{
    private readonly Dictionary<string, CarrierRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _versions = new(StringComparer.Ordinal);

    public InMemoryNodeCarrier(string carrierId, CarrierSemantics semantics = CarrierSemantics.AppendOnly)
    {
        CarrierId = carrierId;
        Semantics = semantics;
    }

    public string CarrierId { get; }

    public CarrierSemantics Semantics { get; }

    public int RecordCount => _records.Count;

    public Task EnsureAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CarrierWriteResult> UpsertAsync(string key, string json, CancellationToken cancellationToken = default)
    {
        var digest = ComputeDigest(json);

        if (_records.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.Digest, digest, StringComparison.Ordinal))
            {
                return Task.FromResult(CarrierWriteResult.Unchanged("same-digest"));
            }

            if (Semantics == CarrierSemantics.AppendOnly)
            {
                _versions.TryGetValue(key, out var versioned);
                versioned ??= new List<string>();
                if (!versioned.Contains(existing.Json, StringComparer.Ordinal))
                {
                    versioned.Add(existing.Json);
                }

                _versions[key] = versioned;
                return Task.FromResult(CarrierWriteResult.Conflict("append-only:different-digest"));
            }
        }

        _records[key] = new CarrierRecord(key, json, digest, DateTimeOffset.UtcNow);
        return Task.FromResult(CarrierWriteResult.Written());
    }

    public Task<CarrierRecord?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_records.TryGetValue(key, out var record) ? record : null);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var removed = _records.Remove(key);
        _versions.Remove(key);
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<string>> EnumerateKeysAsync(string? keyPrefix = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> keys = _records.Keys
            .Where(k => keyPrefix is null || k.StartsWith(keyPrefix, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(keys);
    }

    /// <summary>版本历史（N5：冲突可见化的证据面）。</summary>
    public IReadOnlyList<string> VersionsOf(string key) =>
        _versions.TryGetValue(key, out var versioned) ? versioned.ToArray() : Array.Empty<string>();

    public static string ComputeDigest(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
