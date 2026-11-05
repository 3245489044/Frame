using System;
using System.Collections.Generic;
using System.Net;

namespace Modules.Parsing.Markup;

/// <summary>
/// 方言预置（**新增标记方言的唯一扩展点**）—— 写入器零方言分支，只吃 <see cref="MarkupDialect"/>。
/// </summary>
public static class MarkupDialects
{
    /// <summary>HTML 预置（BCL-only：<see cref="WebUtility.HtmlEncode"/>；布尔属性裸名；空元素斜杠闭合）。</summary>
    public static MarkupDialect Html { get; } = new(
        name: "html",
        encodeText: WebUtility.HtmlEncode,
        encodeAttr: WebUtility.HtmlEncode,
        quote: '"',
        boolStyle: MarkupBoolStyle.Bare,
        voidStyle: MarkupVoidStyle.Slash);

    /// <summary>XML 预置（布尔属性名=值；空元素斜杠闭合；XML 声明头由调用方自行处理）。</summary>
    public static MarkupDialect Xml { get; } = new(
        name: "xml",
        encodeText: WebUtility.HtmlEncode,
        encodeAttr: WebUtility.HtmlEncode,
        quote: '"',
        boolStyle: MarkupBoolStyle.NameValue,
        voidStyle: MarkupVoidStyle.Slash);

    /// <summary>HTML 空元素集（标签名 → true）。仅预设数据，写入器按方言声明决定是否使用。</summary>
    public static IReadOnlySet<string> HtmlVoidTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };
}
