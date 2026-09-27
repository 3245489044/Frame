using Share.HostAssembly.Storage;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 键控快照存储（进程内；按 key 覆盖式读写）—— 底层复用通用内存键值存储
    /// <see cref="InMemoryKeyedStore{TValue}"/>（单一实现）。
    /// <para>空白键守卫：<see cref="Read"/> 返回 null、<see cref="Write"/> 为 no-op（键约定不得由空键承载）。</para>
    /// <para>通用件：零领域类型。</para>
    /// </summary>
    /// <typeparam name="TValue">快照值类型（引用类型）。</typeparam>
    public sealed class KeyedSnapshotStore<TValue> where TValue : class
    {
        private readonly InMemoryKeyedStore<TValue> _store = new();

        /// <summary>读取指定 key 的快照；空白键或不存在 → null。</summary>
        public TValue? Read(string key)
            => string.IsNullOrWhiteSpace(key) ? null : _store.Read(key);

        /// <summary>写入快照（覆盖式）；空白键或 null 值 → no-op。</summary>
        public void Write(string key, TValue? value)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null) return;
            _store.TryWrite(key, value);
        }
    }
}
