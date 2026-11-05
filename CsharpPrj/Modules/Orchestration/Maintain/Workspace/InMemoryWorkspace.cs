#nullable enable

using Modules.Maintain.Ports;

namespace Modules.Maintain.Workspace
{
    /// <summary>
    /// 通用内存工作区：以 <see cref="IDataWorkspace{TData}.GetKey"/> 为键维护数据集合。
    /// 提供默认存储实现（内存背板），供测试与演示使用；
    /// 生产场景可实现自己的 <see cref="IDataWorkspace{TData}"/> 背靠 IChainNodeHandler 等存储。
    /// </summary>
    public sealed class InMemoryWorkspace<TData> : IDataWorkspace<TData>
    {
        private readonly Func<TData, string?> _keyReader;
        private readonly List<TData> _items;

        public InMemoryWorkspace(Func<TData, string?> keyReader)
        {
            _keyReader = keyReader ?? throw new ArgumentNullException(nameof(keyReader));
            _items = new List<TData>();
        }

        public InMemoryWorkspace(Func<TData, string?> keyReader, IEnumerable<TData> seed)
            : this(keyReader)
        {
            if (seed != null) _items.AddRange(seed);
        }

        public IEnumerable<TData> Data => _items;

        public string? GetKey(TData item) => _keyReader(item);

        public void Upsert(TData item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var key = _keyReader(item);
            if (key == null) throw new InvalidOperationException("Upsert 需要非空键。");
            var idx = _items.FindIndex(i => _keyReader(i) == key);
            if (idx >= 0) _items[idx] = item;
            else _items.Add(item);
        }

        public bool Remove(string key)
        {
            var idx = _items.FindIndex(i => _keyReader(i) == key);
            if (idx < 0) return false;
            _items.RemoveAt(idx);
            return true;
        }

        public TData? Find(string key)
            => _items.FirstOrDefault(i => _keyReader(i) == key);

        /// <summary>用一批数据整体替换工作区内容（供 Merge 等整体重建使用）。</summary>
        internal void ReplaceAll(IEnumerable<TData> items)
        {
            _items.Clear();
            _items.AddRange(items);
        }
    }
}