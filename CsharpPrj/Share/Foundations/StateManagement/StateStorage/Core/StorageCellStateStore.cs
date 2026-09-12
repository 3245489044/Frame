
using TConfig.ContextFrame.Abstractions;
using StorageLayer = TConfig.StorageLayer;

namespace Share.StateManagement
{
    /// <summary>
    /// 基于 InternalStorage 抽象（IStorageCell，TConfig 层）的 string KV 状态存储适配器。
    /// 把 <see cref="IStateStore"/> 契约映射到 Session 层，从而复用 IStorageCell 已有的
    /// 分层（Config/Session/Transient）、父链回退、快照与 DataChanged 能力。
    /// 具体 StorageEngine/IStorageCell 实现由组合根注入（InternalStorage.StorageEngine），
    /// Share 层不直接引用 Modules/InternalStorage，保持层级干净（策略 1，TH-12 单一实现）。
    /// </summary>
    public sealed class StorageCellStateStore : IStateStore
    {
        private const StorageLayer Layer = StorageLayer.Session;
        private readonly IStorageCell _cell;
        private readonly string? _excludedKeyPrefix;

        /// <summary>
        /// <paramref name="excludedKeyPrefix"/>：可选——cell 与对象 KV（如 <see cref="ObjectKeyValueStore"/>）共享时，
        /// 枚举类成员（GetAllKeys/GetAll/Count/IsEmpty/RemoveByPrefix）跳过此前缀的键，保持字符串 KV 与对象 KV 隔离。
        /// </summary>
        public StorageCellStateStore(IStorageCell cell, string? excludedKeyPrefix = null)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            _excludedKeyPrefix = excludedKeyPrefix;
        }

        /// <summary>被排除的键前缀（字符串 KV 与对象 KV 共 cell 隔离用）。</summary>
        public string? ExcludedKeyPrefix => _excludedKeyPrefix;

        private bool IsExcluded(string key)
            => _excludedKeyPrefix != null && key.StartsWith(_excludedKeyPrefix, StringComparison.Ordinal);

        /// <summary>被包装的底层存储单元。</summary>
        public IStorageCell Cell => _cell;

        public void Set(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            _cell.Set(key, value, Layer);
        }

        public string? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            return _cell.TryGet<string>(key, out var value, Layer) ? value : null;
        }

        public bool Has(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return _cell.Contains(key, Layer);
        }

        /// <summary>
        /// 清理本层<b>非隔离键</b>（逐键移除；与 <see cref="GetAllKeys"/> 同语义：排除对端前缀，不越界清空）。
        /// 与 DataCenter.Core 端口侧 <c>TierStringStore</c> 对齐修复（F-27/F-36 同构缺陷：原 <c>ClearLayer</c> 越前缀清空）。
        /// </summary>
        public void Clear()
        {
            foreach (var key in GetAllKeys())
                Remove(key);
        }

        public IReadOnlyCollection<string> GetAllKeys() => _cell.GetKeys(Layer).Where(k => !IsExcluded(k)).ToArray();

        public string? GetOrAdd(string key, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be empty", nameof(key));

            if (_cell.Contains(key, Layer))
                return _cell.Get<string>(key, Layer);

            _cell.Set(key, defaultValue, Layer);
            return defaultValue;
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                _cell.Remove(key, Layer);
        }

        public void RemoveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return;

            foreach (var key in GetAllKeys())
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    Remove(key);
                }
            }
        }

        public void SetRange(IEnumerable<KeyValuePair<string, string?>> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            foreach (var kvp in values)
            {
                Set(kvp.Key, kvp.Value);
            }
        }

        public Dictionary<string, string?> GetAll()
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _cell.GetEntries(Layer))
            {
                if (IsExcluded(entry.Key)) continue;
                result[entry.Key] = entry.Value as string;
            }

            return result;
        }

        public int Count => _cell.GetKeys(Layer).Count(k => !IsExcluded(k));

        public bool IsEmpty => !_cell.GetKeys(Layer).Any(k => !IsExcluded(k));
    }
}
