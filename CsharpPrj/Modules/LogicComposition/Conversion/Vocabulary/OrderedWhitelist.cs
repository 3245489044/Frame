using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Vocabulary;

/// <summary>
/// 有序白名单（追加式）：默认项 + 追加项（**只增不改**、按 Ordinal 排序、去重）。
/// 比较一律 Ordinal（对齐大小写敏感的标识符语义）。
/// </summary>
public sealed class OrderedWhitelist
{
    private readonly List<string> _items;

    /// <summary>构造白名单。</summary>
    /// <param name="defaults">默认项（声明序）。</param>
    /// <param name="extra">追加项（可空；只增不改）。</param>
    public OrderedWhitelist(IEnumerable<string> defaults, IEnumerable<string>? extra = null)
    {
        _items = new List<string>(defaults ?? throw new ArgumentNullException(nameof(defaults)));
        AppendOrdered(_items, extra);
    }

    /// <summary>合并后的白名单（声明序 + 追加项排序尾随；用于诊断消息列举）。</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>是否为白名单项。</summary>
    public bool Contains(string name)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i], name, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>列表是否含指定项（Ordinal；供静态白名单复用）。</summary>
    public static bool Contains(IReadOnlyList<string> items, string value)
    {
        if (items is null) return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], value, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static void AppendOrdered(List<string> target, IEnumerable<string>? extra)
    {
        if (extra is null) return;

        var additions = new List<string>();
        foreach (var item in extra)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            if (target.Contains(item) || additions.Contains(item)) continue;
            additions.Add(item);
        }

        additions.Sort(StringComparer.Ordinal);
        target.AddRange(additions);
    }
}
