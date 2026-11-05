using System;
using System.Collections.Generic;

namespace Modules.Parsing.Markup;

/// <summary>
/// HTML 词表单源（空元素 / 布尔属性）—— 解析器、节点序列化器、属性批共用，禁各单位各持一份副本。
/// </summary>
public static class MarkupTerms
{
    /// <summary>HTML 空元素集（与 <see cref="MarkupDialects.HtmlVoidTags"/> 同一实例）。</summary>
    public static IReadOnlySet<string> VoidTags => MarkupDialects.HtmlVoidTags;

    /// <summary>HTML 布尔属性集（值为空即发射裸名，如 <c>checked</c>）。</summary>
    public static IReadOnlySet<string> BooleanAttributes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "open", "disabled", "checked", "selected", "readonly", "required", "multiple", "hidden", "autofocus",
    };
}
