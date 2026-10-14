using System;
using System.Collections.Generic;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 声明式槽宿主 —— 把「有哪些槽」收敛为一次 <see cref="Declare{T}"/> 声明（新槽 = 增一行），
    /// 并提供全槽装配态快照 <see cref="Presence"/>（就绪投影的唯一输入）。
    /// <para>消除宿主逐槽手写 <c>Value/Has/Attach/Detach</c> 三元组与散落的 <c>Has*</c> 布尔：
    /// 各槽句柄由本类统一构造并登记，宿主只保留「声明 + 转发」。</para>
    /// <para>通用件：零领域类型（承载由调用方注入）。</para>
    /// </summary>
    public sealed class SlotHost
    {
        private readonly object _gate;
        private readonly List<ISlotRef> _slots = new();

        /// <param name="gate">跨槽共享锁（调用方持有）。</param>
        public SlotHost(object gate) => _gate = gate ?? throw new ArgumentNullException(nameof(gate));

        /// <summary>声明一个类型化槽并登记进装配态快照。</summary>
        /// <param name="key">槽键。</param>
        /// <param name="carrier">承载解析（同键同实例 holder）。</param>
        /// <param name="lifecycle">装配生命周期钩子（null = 无钩子）。</param>
        public SlotRef<T> Declare<T>(string key, Func<SlotHolder<T>> carrier, ISlotLifecycle? lifecycle = null)
            where T : class
        {
            var slot = new SlotRef<T>(_gate, key, carrier, lifecycle);
            _slots.Add(slot);
            return slot;
        }

        /// <summary>已声明的槽键集（声明序）。</summary>
        public IReadOnlyList<string> Keys
        {
            get
            {
                var list = new List<string>(_slots.Count);
                foreach (var slot in _slots) list.Add(slot.Key);
                return list;
            }
        }

        /// <summary>全槽装配态快照（存在性；就绪投影/诊断唯一输入）。</summary>
        public IReadOnlyList<(string Key, bool Present)> Presence()
        {
            var list = new List<(string, bool)>(_slots.Count);
            foreach (var slot in _slots) list.Add((slot.Key, slot.Has));
            return list;
        }
    }
}
