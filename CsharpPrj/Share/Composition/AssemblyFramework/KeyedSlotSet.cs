using System;
using System.Collections.Generic;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>键控槽集合的承载 holder（可变；由承载解析保证同键同实例）。</summary>
    /// <typeparam name="T">槽值类型（引用类型）。</typeparam>
    public sealed class KeyedHolder<T> where T : class
    {
        /// <summary>键 → 值（覆盖式；缺键即未装配）。</summary>
        public Dictionary<string, T> Values = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 键控槽集合 —— 同一类型 <typeparamref name="T"/> 下按「域槽键」分区承载（集合随实例/承载迁移），
    /// 键集在构造时显式登记（禁任意字符串成为槽键）。消除「同型多域」视图逐域手写四元组的重复
    /// （如 10 个节点域框架视图）。
    /// <para>通用件：零领域类型（键集与承载由调用方注入）。</para>
    /// </summary>
    /// <typeparam name="T">槽值类型（引用类型）。</typeparam>
    public sealed class KeyedSlotSet<T> where T : class
    {
        private readonly object _gate;
        private readonly Func<KeyedHolder<T>> _carrier;
        private readonly string[] _keys;

        /// <param name="gate">跨槽共享锁（调用方持有）。</param>
        /// <param name="keys">已登记键集（显式登记；顺序即 <see cref="Keys"/> 顺序）。</param>
        /// <param name="carrier">承载解析（同键同实例 holder）。</param>
        public KeyedSlotSet(object gate, IEnumerable<string> keys, Func<KeyedHolder<T>> carrier)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            ArgumentNullException.ThrowIfNull(keys);
            _carrier = carrier ?? throw new ArgumentNullException(nameof(carrier));
            var list = new List<string>();
            foreach (var key in keys)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                list.Add(key);
            }
            _keys = list.ToArray();
        }

        /// <summary>已登记键集（装配对账 / 测试用；登记序）。</summary>
        public IReadOnlyList<string> Keys => _keys;

        /// <summary>是否为已登记键。</summary>
        public bool IsKey(string? key) => key is not null && Array.IndexOf(_keys, key) >= 0;

        /// <summary>读某键视图（未登记 / 未装配 ⇒ null）。</summary>
        public T? Get(string key)
        {
            lock (_gate)
                return _carrier().Values.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>某键是否已装配。</summary>
        public bool Has(string key) => Get(key) is not null;

        /// <summary>装配某键视图（幂等覆盖式）；未登记键 ⇒ false（不静默）。</summary>
        public bool Attach(string key, T value)
        {
            if (!IsKey(key)) return false;
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate) _carrier().Values[key] = value;
            return true;
        }

        /// <summary>解某键视图（不释放其资源）；未登记键 ⇒ false。</summary>
        public bool Detach(string key)
        {
            if (!IsKey(key)) return false;
            lock (_gate) return _carrier().Values.Remove(key);
        }

        /// <summary>键集装配态快照（存在性；供就绪投影/诊断）。</summary>
        public IReadOnlyList<(string Key, bool Present)> Presence()
        {
            lock (_gate)
            {
                var values = _carrier().Values;
                var list = new List<(string, bool)>(_keys.Length);
                foreach (var key in _keys) list.Add((key, values.ContainsKey(key)));
                return list;
            }
        }
    }
}
