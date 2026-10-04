using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Share.StateManagement
{
    /// <summary>
    /// 对象键值存储（带键前缀隔离）——基于 <see cref="IStorageCell"/> 的强类型对象 KV。
    /// 键加前缀落 Session 层，与字符串 KV（如 <see cref="StorageCellStateStore"/>）共享同一 cell 时互不覆盖
    /// （原 StateStore「双字典」语义）。GetAll 为只读快照（ExtendedStates 语义）。
    /// </summary>
    public sealed class ObjectKeyValueStore
    {
        private const StorageLayer Layer = StorageLayer.Session;
        private readonly IStorageCell _cell;
        private readonly string _keyPrefix;

        /// <summary>对象 KV 键前缀。</summary>
        public string Prefix => _keyPrefix;

        /// <param name="keyPrefix">键前缀（对象键 = 前缀 + key）；须与同 cell 的 StorageCellStateStore.excludedKeyPrefix 一致。</param>
        public ObjectKeyValueStore(IStorageCell cell, string keyPrefix)
        {
            _cell = PortGuard.Required(cell, nameof(cell));
            _keyPrefix = PortGuard.Required(keyPrefix, nameof(keyPrefix));
            if (keyPrefix.Length == 0)
                throw new ArgumentException("Key prefix cannot be empty", nameof(keyPrefix));
        }

        public TState? Get<TState>(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return _cell.Get<TState>(_keyPrefix + key, Layer);
        }

        public void Set<TState>(string key, TState value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _cell.Set(_keyPrefix + key, value, Layer);
        }

        public bool Contains(string key)
            => !string.IsNullOrWhiteSpace(key) && _cell.Contains(_keyPrefix + key, Layer);

        public void Remove(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _cell.Remove(_keyPrefix + key, Layer);
        }

        /// <summary>清空全部对象 KV（仅此前缀的键），不动字符串 KV。</summary>
        public void Clear()
        {
            foreach (var key in _cell.GetKeys(Layer)
                         .Where(k => k.StartsWith(_keyPrefix, StringComparison.Ordinal)).ToList())
            {
                _cell.Remove(key, Layer);
            }
        }

        /// <summary>全部对象 KV 只读快照（键已去前缀）。</summary>
        public IReadOnlyDictionary<string, object> GetAll()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kvp in _cell.GetEntries(Layer))
            {
                if (kvp.Key.StartsWith(_keyPrefix, StringComparison.Ordinal) && kvp.Value != null)
                {
                    result[kvp.Key.Substring(_keyPrefix.Length)] = kvp.Value;
                }
            }
            return result;
        }
    }
}
