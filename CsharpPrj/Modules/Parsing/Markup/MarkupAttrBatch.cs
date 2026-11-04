using System;
using System.Collections.Generic;
using System.Text;

namespace Modules.Parsing.Markup;

/// <summary>
/// 属性批（**属性发射的唯一实现**）—— 顺序即插入序（DOM 属性顺序是现有快照断言的一部分，不得重排）；
/// 名字校验与值编码各恰一处（<see cref="MarkupDialect"/> 声明）；非法名字 fail-fast。
/// <para>
/// 语义分档：<see cref="Add"/> 只跳 <c>null</c>（<c>""</c> 合法，产出 <c>name=""</c>）；
/// <see cref="AddIfAny"/> 跳 <c>null</c>/空串（旧代码的"有值才产"口径）。
/// </para>
/// </summary>
public sealed class MarkupAttrBatch
{
    private readonly List<KeyValuePair<string, string?>> _items = new();
    private readonly MarkupDialect _dialect;

    /// <param name="dialect">方言；null ⇒ <see cref="MarkupDialects.Html"/>。</param>
    public MarkupAttrBatch(MarkupDialect? dialect = null) => _dialect = dialect ?? MarkupDialects.Html;

    /// <summary>本批使用的方言。</summary>
    public MarkupDialect Dialect => _dialect;

    /// <summary>属性条数。</summary>
    public int Count => _items.Count;

    /// <summary>是否为空。</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>追加属性；<paramref name="value"/> 为 <c>null</c> ⇒ 不产；<c>""</c> ⇒ 产 <c>name=""</c>。</summary>
    public MarkupAttrBatch Add(string name, string? value)
    {
        _dialect.EnsureAttrName(name);
        if (value is null) return this;
        _items.Add(new KeyValuePair<string, string?>(name, _dialect.EncodeAttr(value)));
        return this;
    }

    /// <summary>追加属性；<paramref name="value"/> 为 null/空串 ⇒ 不产。</summary>
    public MarkupAttrBatch AddIfAny(string name, string? value)
        => string.IsNullOrEmpty(value) ? this : Add(name, value);

    /// <summary>追加裸属性（布尔形态，如 <c>checked</c>）。</summary>
    public MarkupAttrBatch Bare(string name)
    {
        _dialect.EnsureAttrName(name);
        _items.Add(new KeyValuePair<string, string?>(name, null));
        return this;
    }

    /// <summary>布尔属性：按方言形态发射（<see cref="MarkupBoolStyle"/>）；<paramref name="on"/>=false ⇒ 不产。</summary>
    public MarkupAttrBatch Bool(string name, bool on)
    {
        if (!on) return this;
        return _dialect.BoolStyle == MarkupBoolStyle.Bare ? Bare(name) : Add(name, name);
    }

    /// <summary>类属性：空/空白 ⇒ 回落 <paramref name="fallback"/>（与 <c>TextUtil.CssClass</c> 同口径）。</summary>
    public MarkupAttrBatch Class(string? cssClass, string fallback)
        => Add(_dialect.ClassAttr, string.IsNullOrWhiteSpace(cssClass) ? fallback : cssClass);

    /// <summary>稳定名属性（<see cref="MarkupDialect.NameAttr"/>）；null/空 ⇒ 不产。</summary>
    public MarkupAttrBatch Name(string? name) => AddIfAny(_dialect.NameAttr, name);

    /// <summary>差分键属性（<see cref="MarkupDialect.KeyAttr"/>）；null/空 ⇒ 不产。</summary>
    public MarkupAttrBatch Key(string? key) => AddIfAny(_dialect.KeyAttr, key);

    /// <summary><c>data-*</c> 属性（键按方言文法校验；空键 ⇒ 跳过，值 null ⇒ 空串）。</summary>
    public MarkupAttrBatch Data(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return this;
        _dialect.EnsureDataName(key);
        _items.Add(new KeyValuePair<string, string?>(_dialect.DataPrefix + key, _dialect.EncodeAttr(value ?? string.Empty)));
        return this;
    }

    /// <summary>批量 <c>data-*</c>（保字典枚举顺序；空键跳过）。</summary>
    public MarkupAttrBatch DataAll(IEnumerable<KeyValuePair<string, string>>? data)
    {
        if (data is null) return this;
        foreach (var kv in data) Data(kv.Key, kv.Value);
        return this;
    }

    /// <summary>渲染为属性串（前导空格；空批 ⇒ 空串）。</summary>
    public override string ToString() => Render();

    /// <summary>渲染为属性串（前导空格；空批 ⇒ 空串）。</summary>
    public string Render()
    {
        if (_items.Count == 0) return string.Empty;
        var sb = new StringBuilder(_items.Count * 16);
        foreach (var item in _items)
        {
            sb.Append(' ').Append(item.Key);
            if (item.Value is null) continue;
            sb.Append('=').Append(_dialect.Quote).Append(item.Value).Append(_dialect.Quote);
        }

        return sb.ToString();
    }
}
