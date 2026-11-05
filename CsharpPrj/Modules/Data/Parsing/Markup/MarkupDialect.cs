using System;
using System.Text.RegularExpressions;

namespace Modules.Parsing.Markup;

/// <summary>布尔属性发射形态（方言差异的唯一分叉点）。</summary>
public enum MarkupBoolStyle
{
    /// <summary>裸名（HTML：<c>checked</c>）。</summary>
    Bare = 0,

    /// <summary>名=值（XHTML/XML：<c>checked="checked"</c>）。</summary>
    NameValue = 1,
}

/// <summary>空元素闭合形态。</summary>
public enum MarkupVoidStyle
{
    /// <summary>斜杠闭合（<c>&lt;input/&gt;</c>）。</summary>
    Slash = 0,

    /// <summary>仅右尖括号（<c>&lt;input&gt;</c>）。</summary>
    Plain = 1,
}

/// <summary>
/// 标记方言描述符（**数据，不是分支**）—— 把引号/转义/属性名文法/布尔形态/空元素形态集中声明，
/// 使写入器代码里零 <c>if (html)</c>：新增一种方言 = 新增一个预置实例，写入器一行不改。
/// <para>
/// 判据：方言只描述"怎么把名字与值写成合法标记"，不描述"写什么内容"（结构/槽位/语义仍在宿主）。
/// </para>
/// </summary>
public sealed class MarkupDialect
{
    /// <summary>默认属性名文法：字母/下划线起首，允许字母数字与 <c>_.:-</c>（含命名空间前缀），禁空白/引号/等号/尖括号。</summary>
    public const string DefaultAttrNamePattern = @"^[A-Za-z_][A-Za-z0-9_.:-]*$";

    /// <summary>默认 <c>data-</c> 后缀名文法：字母数字/下划线起首，允许字母数字与 <c>_.-</c>（禁冒号，避免与命名空间前缀混淆）。</summary>
    public const string DefaultDataNamePattern = @"^[A-Za-z0-9_][A-Za-z0-9_.-]*$";

    /// <summary>默认标签名文法：字母起首，允许字母数字与 <c>-</c>。</summary>
    public const string DefaultTagNamePattern = @"^[A-Za-z][A-Za-z0-9-]*$";

    private readonly Regex _attrName;
    private readonly Regex _dataName;
    private readonly Regex _tagName;

    /// <param name="name">方言名（诊断用）。</param>
    /// <param name="encodeText">文本编码（元素内容位）。</param>
    /// <param name="encodeAttr">属性值编码（属性值位）。</param>
    /// <param name="quote">属性值引号（默认双引号）。</param>
    /// <param name="attrNamePattern">属性名文法。</param>
    /// <param name="dataNamePattern"><c>data-</c> 后缀名文法。</param>
    /// <param name="tagNamePattern">标签名文法。</param>
    /// <param name="dataPrefix"><c>data-</c> 前缀。</param>
    /// <param name="classAttr">类属性名。</param>
    /// <param name="nameAttr">稳定名属性名。</param>
    /// <param name="keyAttr">差分键属性名。</param>
    /// <param name="boolStyle">布尔属性形态。</param>
    /// <param name="voidStyle">空元素闭合形态。</param>
    public MarkupDialect(
        string name,
        Func<string, string> encodeText,
        Func<string, string> encodeAttr,
        char quote = '"',
        string attrNamePattern = DefaultAttrNamePattern,
        string dataNamePattern = DefaultDataNamePattern,
        string tagNamePattern = DefaultTagNamePattern,
        string dataPrefix = "data-",
        string classAttr = "class",
        string nameAttr = "data-name",
        string keyAttr = "data-key",
        MarkupBoolStyle boolStyle = MarkupBoolStyle.Bare,
        MarkupVoidStyle voidStyle = MarkupVoidStyle.Slash)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("方言名不得为空。", nameof(name)) : name;
        EncodeText = encodeText ?? throw new ArgumentNullException(nameof(encodeText));
        EncodeAttr = encodeAttr ?? throw new ArgumentNullException(nameof(encodeAttr));
        Quote = quote;
        DataPrefix = dataPrefix ?? string.Empty;
        ClassAttr = classAttr;
        NameAttr = nameAttr;
        KeyAttr = keyAttr;
        BoolStyle = boolStyle;
        VoidStyle = voidStyle;
        _attrName = new Regex(attrNamePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        _dataName = new Regex(dataNamePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        _tagName = new Regex(tagNamePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    /// <summary>方言名（诊断 / 单测）。</summary>
    public string Name { get; }

    /// <summary>文本编码（元素内容位）。</summary>
    public Func<string, string> EncodeText { get; }

    /// <summary>属性值编码（属性值位）。</summary>
    public Func<string, string> EncodeAttr { get; }

    /// <summary>属性值引号。</summary>
    public char Quote { get; }

    /// <summary><c>data-</c> 前缀。</summary>
    public string DataPrefix { get; }

    /// <summary>类属性名。</summary>
    public string ClassAttr { get; }

    /// <summary>稳定名属性名。</summary>
    public string NameAttr { get; }

    /// <summary>差分键属性名。</summary>
    public string KeyAttr { get; }

    /// <summary>布尔属性形态。</summary>
    public MarkupBoolStyle BoolStyle { get; }

    /// <summary>空元素闭合形态。</summary>
    public MarkupVoidStyle VoidStyle { get; }

    /// <summary>校验属性名；非法 ⇒ <see cref="ArgumentException"/>（fail-fast，不静默丢弃）。</summary>
    public void EnsureAttrName(string name)
    {
        if (string.IsNullOrEmpty(name) || !_attrName.IsMatch(name))
            throw new ArgumentException("非法属性名：" + (name ?? "<null>"), nameof(name));
    }

    /// <summary>校验 <c>data-</c> 后缀名；非法 ⇒ <see cref="ArgumentException"/>。</summary>
    public void EnsureDataName(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !_dataName.IsMatch(key))
            throw new ArgumentException("非法 data 属性名：" + (key ?? "<null>"), nameof(key));
    }

    /// <summary>校验标签名；非法 ⇒ <see cref="ArgumentException"/>。</summary>
    public void EnsureTagName(string tag)
    {
        if (string.IsNullOrEmpty(tag) || !_tagName.IsMatch(tag))
            throw new ArgumentException("非法标签名：" + (tag ?? "<null>"), nameof(tag));
    }
}
