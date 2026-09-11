using DataCenter.Abstractions.Cells;
using Operation.StateBlock.Core;
using Operation.StateBlock.Core.Compat;

using Share.StateManagement;
using TConfig;
using TConfig.ContextFrame.Abstractions;

namespace Operation.StateBlock.Layering
{
    /// <summary>
    /// 分层状态读写客户端 —— 把「单键读写 + 分区枚举/清理 + 存在性探测」收进框架，作为上层状态访问的 canonical 通道。
    /// 单键读写委托 <see cref="IStateCenter.Read{TState}(StateKey, StorageLayer)"/> /
    /// <see cref="IStateCenter.Write{TState}(StateKey, TState, StorageLayer, string?)"/>；
    /// 枚举/清理/存在性经端口数据单元（<see cref="IDataCell"/>，取自中心 <see cref="IDataCellProvider"/>；
    /// 中心非端口实现时经 Compat 适配器接入），前缀剥离在本类内部完成。
    /// 上层只依赖 <see cref="StateLayerScheme"/> + <see cref="LayeredStateKey"/>，不再持 StateCenter 或裸存储契约、
    /// 不再手工拼接 / 剥离 <c>ns:scope:</c> 前缀。
    /// </summary>
    public sealed class LayeredStoreClient
    {
        private readonly IStateCenter _center;
        private readonly StateLayerScheme _scheme;
        private readonly IDataCell _dataCell;

        public LayeredStoreClient(IStateCenter center, StateLayerScheme scheme)
        {
            _center = center ?? throw new ArgumentNullException(nameof(center));
            _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
            _dataCell = center is IDataCellProvider provider
                ? provider.Root
                : new Operation.StateBlock.Core.Compat.StorageCellAdapter(center.Cell);
        }

        /// <summary>底层分层存储单元（契约视图）；只读供高级枚举/批量操作，不做裸键访问。</summary>
        public IStorageCell Cell => _center.Cell;

        public StateLayerScheme Scheme => _scheme;

        public LayeredStateKey Key(StorageLayer layer, string scope, string key) => _scheme.Key(layer, scope, key);

        // ── 单键读写（走 StateCenter 统一门面） ──

        public T? Read<T>(LayeredStateKey key)
            => _center.Read<T>(key.ToStateKey(), key.Layer);

        public T? Read<T>(StorageLayer layer, string scope, string key)
            => Read<T>(_scheme.Key(layer, scope, key));

        public void Write<T>(LayeredStateKey key, T value, string? source = null)
            => _center.Write(key.ToStateKey(), value, key.Layer, source);

        public void Write<T>(StorageLayer layer, string scope, string key, T value, string? source = null)
            => Write(_scheme.Key(layer, scope, key), value, source);

        // ── 存在性 / 移除（经端口数据单元，键已含 ns:scope: 前缀） ──

        public bool TryGet<T>(LayeredStateKey key, out T? value)
            => _dataCell.TryGet(key.FullKey, out value, key.Layer.ToTier());

        public bool Contains(LayeredStateKey key)
            => _dataCell.Contains(key.FullKey, key.Layer.ToTier());

        public bool Remove(LayeredStateKey key)
            => _dataCell.Remove(key.FullKey, key.Layer.ToTier());

        // ── 分区枚举 / 清理（按 PartitionPrefix 过滤并剥离前缀） ──

        public IEnumerable<string> Keys(StorageLayer layer, string scope)
        {
            var prefix = _scheme.Key(layer, scope, string.Empty).PartitionPrefix;
            return _dataCell.GetKeys(layer.ToTier())
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Select(k => k.Substring(prefix.Length));
        }

        public IEnumerable<KeyValuePair<string, object?>> Entries(StorageLayer layer, string scope)
        {
            var prefix = _scheme.Key(layer, scope, string.Empty).PartitionPrefix;
            return _dataCell.GetEntries(layer.ToTier())
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => new KeyValuePair<string, object?>(kv.Key.Substring(prefix.Length), kv.Value));
        }

        /// <summary>清理指定 scope 分区（仅移除本前缀下键，不影响同层其他 scope）。</summary>
        public void ClearPartition(StorageLayer layer, string scope)
        {
            foreach (var key in Keys(layer, scope).ToArray())
                Remove(_scheme.Key(layer, scope, key));
        }

        /// <summary>清理整个层（对齐底层 ClearLayer 语义；多 scope 共存时慎用）。</summary>
        public void ClearLayer(StorageLayer layer) => _dataCell.ClearTier(layer.ToTier());
    }
}
