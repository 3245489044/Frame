
using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Models.Ir
{
    /// <summary>数组节点（有序）。</summary>
    public sealed class ConversionArray : ConversionNode
    {
        private readonly List<ConversionNode> _items = new List<ConversionNode>();

        public override ConversionNodeKind Kind => ConversionNodeKind.Array;

        public IReadOnlyList<ConversionNode> Items => _items;

        public int Count => _items.Count;

        public ConversionArray Add(ConversionNode item)
        {
            _items.Add(item ?? ConversionScalar.Null);
            return this;
        }

        public ConversionArray Add(string? item) => Add(ConversionScalar.Of(item));

        public ConversionArray AddRange(IEnumerable<ConversionNode> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            foreach (var item in items) Add(item);
            return this;
        }

        public static ConversionArray Of(params ConversionNode[] items)
        {
            var array = new ConversionArray();
            array.AddRange(items ?? Array.Empty<ConversionNode>());
            return array;
        }

        public static ConversionArray OfStrings(IEnumerable<string?> values)
        {
            var array = new ConversionArray();
            if (values is null) return array;
            foreach (var value in values) array.Add(ConversionScalar.Of(value));
            return array;
        }

        /// <summary>读取字符串元素（非字符串元素跳过）。</summary>
        public IEnumerable<string> Strings()
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i] is ConversionScalar { ScalarKind: ConversionScalarKind.String } scalar
                    && scalar.Value is string text)
                {
                    yield return text;
                }
            }
        }
    }
}
