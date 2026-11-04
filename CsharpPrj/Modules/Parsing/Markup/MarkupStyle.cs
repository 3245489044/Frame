using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Modules.Parsing.Markup;

/// <summary>
/// 内联样式序列化（**有序声明，不是 CSS 解析/构建器**）—— 把 <c>position:absolute;left:10px;</c> 的拼接收成单实现。
/// <para>纪律：只校验属性名文法（小写字母/连字符），不做 CSS 语义校验；值原样发射，属性位编码由
/// <see cref="MarkupAttrBatch.Add"/> 负责（避免样式值突破属性边界）。</para>
/// </summary>
public sealed class MarkupStyle
{
    private static readonly Regex NamePattern =
        new("^[a-z][a-z-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<KeyValuePair<string, string>> _items = new();

    /// <summary>追加一条声明；<paramref name="value"/> 为 null ⇒ 不产。</summary>
    public MarkupStyle Add(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name) || !NamePattern.IsMatch(name))
            throw new ArgumentException("非法样式属性名：" + (name ?? "<null>"), nameof(name));
        if (value is null) return this;
        _items.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    /// <summary>追加像素声明（<c>name:{px}px</c>）。</summary>
    public MarkupStyle AddPx(string name, int value)
        => Add(name, value.ToString(CultureInfo.InvariantCulture) + "px");

    /// <summary>声明条数。</summary>
    public int Count => _items.Count;

    /// <summary>序列化为内联样式串（顺序 = 插入序；空 ⇒ 空串）。</summary>
    public string Render()
    {
        if (_items.Count == 0) return string.Empty;
        var sb = new StringBuilder(_items.Count * 24);
        foreach (var item in _items)
            sb.Append(item.Key).Append(':').Append(item.Value).Append(';');
        return sb.ToString();
    }

    /// <summary>序列化为内联样式串。</summary>
    public override string ToString() => Render();
}
