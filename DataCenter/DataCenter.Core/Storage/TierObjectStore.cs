using DataCenter.Abstractions.Cells;

namespace Operation.StateBlock.Core.Storage;

/// <summary>
/// 端口层对象 KV（Session 层；键前缀隔离）—— 替代 Share 侧基于 IStorageCell 的实现。
/// 语义对齐：键加前缀落 Session 层，与字符串 KV 共单元时互不覆盖（原 StateStore「双字典」语义）；
/// GetAll 为只读快照（ExtendedStates 语义，键已去前缀）。
/// </summary>
internal sealed class TierObjectStore
{
    private const DataTier Tier = DataTier.Session;

    private readonly IDataCell _cell;
    private readonly string _keyPrefix;

    internal TierObjectStore(IDataCell cell, string keyPrefix)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        if (keyPrefix.Length == 0)
            throw new ArgumentException("Key prefix cannot be empty", nameof(keyPrefix));
    }

    public string Prefix => _keyPrefix;

    public TState? Get<TState>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _cell.Get<TState>(_keyPrefix + key, Tier);
    }

    public void Set<TState>(string key, TState value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _cell.Set(_keyPrefix + key, value, Tier);
    }

    public bool Contains(string key)
        => !string.IsNullOrWhiteSpace(key) && _cell.Contains(_keyPrefix + key, Tier);

    public void Remove(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _cell.Remove(_keyPrefix + key, Tier);
    }

    public void Clear()
    {
        foreach (var key in _cell.GetKeys(Tier)
                     .Where(k => k.StartsWith(_keyPrefix, StringComparison.Ordinal)).ToList())
        {
            _cell.Remove(key, Tier);
        }
    }

    public IReadOnlyDictionary<string, object> GetAll()
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kvp in _cell.GetEntries(Tier))
        {
            if (kvp.Key.StartsWith(_keyPrefix, StringComparison.Ordinal) && kvp.Value != null)
                result[kvp.Key.Substring(_keyPrefix.Length)] = kvp.Value;
        }
        return result;
    }
}
