using System;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 键控槽视图句柄 —— 把「键控槽集合 + 单键」绑定为一个具名访问器（<see cref="Value"/> / <see cref="Has"/> /
    /// <see cref="Attach"/> / <see cref="Detach"/>）。使 N 个同型槽位各以**一行声明**获得完整装配面，
    /// 免去逐槽重复定义「是否 / 读取 / 装配 / 解除」四元组。
    /// <para>与 <see cref="KeyedSlotSet{T}"/> 分工：集合 = 多键承载与键登记；本类 = 单键具名句柄。</para>
    /// <para>通用件：零领域类型（集合与键由调用方注入）。</para>
    /// </summary>
    /// <typeparam name="T">槽值类型（引用类型）。</typeparam>
    public sealed class KeyedSlotView<T> where T : class
    {
        private readonly KeyedSlotSet<T> _set;

        /// <param name="set">所属键控槽集合（承载共享）。</param>
        /// <param name="key">本视图绑定的槽键（须在集合已登记）。</param>
        public KeyedSlotView(KeyedSlotSet<T> set, string key)
        {
            _set = set ?? throw new ArgumentNullException(nameof(set));
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        /// <summary>绑定的槽键。</summary>
        public string Key { get; }

        /// <summary>当前值；未装配为 null。</summary>
        public T? Value => _set.Get(Key);

        /// <summary>是否已装配。</summary>
        public bool Has => _set.Has(Key);

        /// <summary>装配（幂等覆盖式）。传入需非 null。</summary>
        public void Attach(T value) => _set.Attach(Key, value);

        /// <summary>解除装配（不释放其资源）。</summary>
        public void Detach() => _set.Detach(Key);
    }
}
