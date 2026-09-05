using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Share.HierarchySupport
{
    /// <summary>
    /// 类型化数据空间 —— 统一数据传递标准的唯一物理容器（根快照 / 轮内数据空间复用本类）。
    /// 同时实现 <see cref="IDataSpace"/>（类型化读写 + 契约校验）与 <see cref="IDictionary{TKey,TValue}"/>
    /// （兼容统一上下文 / 公理引擎 / 快照序列化器的字典视图），成为跨 Root / Vars / AxiomContext.Data 的单一数据载体。
    /// <paramref name="validate"/> = true 时写前按 <see cref="DataContractRegistry"/> 校验「已注册 + 类型匹配」；
    /// = false 时仅做纯字典传递（骨架临时空间 / 测试直连）。<paramref name="audit"/> 为宿主审计钩子（key + action）。
    /// </summary>
    public sealed class DataSpace : IDataSpace, IDictionary<string, object>
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);
        private readonly bool _validate;
        private readonly Action<string, string, string?>? _audit;

        public DataSpace(bool validate = true, Action<string, string, string?>? audit = null)
        {
            _validate = validate;
            _audit = audit;
        }

        /// <summary>契约校验模式是否开启。</summary>
        public bool Validate => _validate;

        // ---------- IDataSpace：类型化通道（唯一业务读写入口） ----------

        public T? Get<T>(string key)
        {
            if (!_data.TryGetValue(key, out var v)) return default;
            if (v is not T typed)
                throw new InvalidOperationException(
                    $"数据键 '{key}' 实际类型 {v?.GetType().Name ?? "null"}，期望 {typeof(T).Name}。");
            return typed;
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (_data.TryGetValue(key, out var v) && v is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value) => SetCore(key, value);

        public void Set(string key, object? value) => SetCore(key, value);

        private void SetCore(string key, object? value)
        {
            if (_validate)
            {
                if (!DataContractRegistry.TryGet(key, out var contract))
                    throw new InvalidOperationException(
                        $"数据契约未注册: '{key}'。请先在 DataContractRegistry 注册，再经数据空间传递。");
                if (!contract.Accepts(value))
                    throw new InvalidOperationException(
                        $"数据契约 '{key}' 声明类型 {contract.ValueType.Name}，实际传入 {value?.GetType().Name ?? "null"}。");
            }
            _data[key] = value;
            _audit?.Invoke(key, "set", value?.GetType().Name);
        }

        public bool ContainsKey(string key) => _data.ContainsKey(key);

        public bool Remove(string key)
        {
            var removed = _data.Remove(key);
            if (removed) _audit?.Invoke(key, "remove", null);
            return removed;
        }

        public int Count => _data.Count;

        public IReadOnlyDictionary<string, object?> Snapshot() => _data;

        // ---------- IDictionary<string, object>：兼容统一上下文 / 公理引擎 / 快照序列化器（显式，非空桥接） ----------

        object IDictionary<string, object>.this[string key]
        {
            get => _data[key]!;
            set => _data[key] = value;
        }

        ICollection<string> IDictionary<string, object>.Keys => _data.Keys;

        ICollection<object> IDictionary<string, object>.Values
        {
            get
            {
                var list = new List<object>(_data.Count);
                foreach (var v in _data.Values) list.Add(v!);
                return list;
            }
        }

        bool IDictionary<string, object>.TryGetValue(string key, out object value)
        {
            if (_data.TryGetValue(key, out var v))
            {
                value = v!;
                return true;
            }
            value = null!;
            return false;
        }

        void IDictionary<string, object>.Add(string key, object value) => _data.Add(key, value);

        void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item) => _data.Add(item.Key, item.Value);

        void ICollection<KeyValuePair<string, object>>.Clear() => _data.Clear();

        bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item)
        {
            return _data.TryGetValue(item.Key, out var v) && Equals(v, item.Value);
        }

        void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));
            foreach (var kv in _data)
                array[arrayIndex++] = new KeyValuePair<string, object>(kv.Key, kv.Value!);
        }

        bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item)
        {
            if (!_data.TryGetValue(item.Key, out var v) || !Equals(v, item.Value)) return false;
            return _data.Remove(item.Key);
        }

        bool ICollection<KeyValuePair<string, object>>.IsReadOnly => false;

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            foreach (var kv in _data)
                yield return new KeyValuePair<string, object>(kv.Key, kv.Value!);
        }

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<KeyValuePair<string, object>>)this).GetEnumerator();
    }
}

