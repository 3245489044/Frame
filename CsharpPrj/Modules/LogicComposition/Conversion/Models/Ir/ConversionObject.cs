
using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Models.Ir
{
    /// <summary>
    /// 对象节点：**有序**键值对（默认保留插入序，保证产物顺序可复现、diff 稳定）。
    /// </summary>
    public sealed class ConversionObject : ConversionNode
    {
        private readonly List<KeyValuePair<string, ConversionNode>> _members
            = new List<KeyValuePair<string, ConversionNode>>();

        public override ConversionNodeKind Kind => ConversionNodeKind.Object;

        public IReadOnlyList<KeyValuePair<string, ConversionNode>> Members => _members;

        public int Count => _members.Count;

        public ConversionObject Add(string key, ConversionNode value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            _members.Add(new KeyValuePair<string, ConversionNode>(key, value ?? ConversionScalar.Null));
            return this;
        }

        /// <summary>添加字符串成员（null → JSON null）。</summary>
        public ConversionObject Add(string key, string? value) => Add(key, ConversionScalar.Of(value));

        public ConversionObject Add(string key, bool value) => Add(key, ConversionScalar.Of(value));

        public ConversionObject Add(string key, long value) => Add(key, ConversionScalar.Of(value));

        public ConversionObject Add(string key, int value) => Add(key, ConversionScalar.Of(value));

        public ConversionObject Add(string key, double value) => Add(key, ConversionScalar.Of(value));

        /// <summary>添加数组成员。</summary>
        public ConversionObject Add(string key, IEnumerable<ConversionNode> values)
        {
            var array = new ConversionArray();
            array.AddRange(values);
            return Add(key, array);
        }

        /// <summary>添加字符串数组成员。</summary>
        public ConversionObject AddStrings(string key, IEnumerable<string?> values)
            => Add(key, ConversionArray.OfStrings(values));

        /// <summary>显式添加 null 成员（规避 Add(string, null) 的重载歧义）。</summary>
        public ConversionObject AddNull(string key) => Add(key, ConversionScalar.Null);

        /// <summary>移除首个同名成员（返回是否存在）。</summary>
        public bool Remove(string key)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (string.Equals(_members[i].Key, key, StringComparison.Ordinal))
                {
                    _members.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool TryGet(string key, out ConversionNode value)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (string.Equals(_members[i].Key, key, StringComparison.Ordinal))
                {
                    value = _members[i].Value;
                    return true;
                }
            }
            value = ConversionScalar.Null;
            return false;
        }

        public ConversionNode? Get(string key) => TryGet(key, out var value) ? value : null;

        /// <summary>读取字符串成员（不存在或非字符串返回 null）。</summary>
        public string? GetString(string key)
            => TryGet(key, out var value) && value is ConversionScalar { ScalarKind: ConversionScalarKind.String } scalar
                ? (string?)scalar.Value
                : null;

        public IEnumerable<string> Keys
        {
            get
            {
                for (var i = 0; i < _members.Count; i++)
                {
                    yield return _members[i].Key;
                }
            }
        }
    }
}
