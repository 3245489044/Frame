using DataCenter.Abstractions.Cells;

namespace Operation.StateBlock.Core.Storage;

/// <summary>
/// 端口层字符串 KV（Session 层；可选排除前缀与对象 KV 隔离）—— 替代 Share 侧基于 IStorageCell 的实现，
/// 语义对齐：Set/Get/Has/Clear/GetAllKeys/GetOrAdd/Remove/RemoveByPrefix/SetRange/GetAll/Count/IsEmpty。
/// </summary>
internal sealed class TierStringStore
{
    private const DataTier Tier = DataTier.Session;

    private readonly IDataCell _cell;
    private readonly string? _excludedKeyPrefix;

    internal TierStringStore(IDataCell cell, string? excludedKeyPrefix = null)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _excludedKeyPrefix = excludedKeyPrefix;
    }

    internal string? ExcludedKeyPrefix => _excludedKeyPrefix;

    private bool IsExcluded(string key)
        => _excludedKeyPrefix != null && key.StartsWith(_excludedKeyPrefix, StringComparison.Ordinal);

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        _cell.Set(key, value, Tier);
    }

    public string? Get(string key)
        => string.IsNullOrWhiteSpace(key) ? null
         : _cell.TryGet<string>(key, out var value, Tier) ? value : null;

    public bool Has(string key) => !string.IsNullOrWhiteSpace(key) && _cell.Contains(key, Tier);

    /// <summary>
    /// 清理本层<b>非隔离键</b>（逐键移除；与 <see cref="GetAllKeys"/> 同语义：排除对端前缀，不越界清空）。
    /// 原实现直接 <c>ClearTier</c> 会连排除前缀（对象态）一并清掉 —— 越界清空（F-36）。
    /// </summary>
    public void Clear()
    {
        foreach (var key in GetAllKeys())
            Remove(key);
    }

    public IReadOnlyCollection<string> GetAllKeys() => _cell.GetKeys(Tier).Where(k => !IsExcluded(k)).ToArray();

    public string? GetOrAdd(string key, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        if (_cell.Contains(key, Tier))
            return _cell.Get<string>(key, Tier);

        _cell.Set(key, defaultValue, Tier);
        return defaultValue;
    }

    public void Remove(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _cell.Remove(key, Tier);
    }

    public void RemoveByPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return;

        foreach (var key in GetAllKeys())
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                Remove(key);
        }
    }

    public void SetRange(IEnumerable<KeyValuePair<string, string?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var kvp in values)
            Set(kvp.Key, kvp.Value);
    }

    public Dictionary<string, string?> GetAll()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _cell.GetEntries(Tier))
        {
            if (IsExcluded(entry.Key)) continue;
            result[entry.Key] = entry.Value as string;
        }
        return result;
    }

    public int Count => _cell.GetKeys(Tier).Count(k => !IsExcluded(k));

    public bool IsEmpty => !_cell.GetKeys(Tier).Any(k => !IsExcluded(k));
}
