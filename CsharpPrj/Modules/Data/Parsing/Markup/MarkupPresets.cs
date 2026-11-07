using System;

namespace Modules.Parsing.Markup;

/// <summary>
/// 页面级命名约定预置（**纯字符串约定，含零领域语义**）—— 只收敛"同一段壳被手拼 N 次"的重复，
/// 不接管槽位语义（哪些槽存在/如何嵌套仍由宿主的视图程序声明决定）。
/// </summary>
public static class MarkupPresets
{
    /// <summary>区域容器类名。</summary>
    public const string RegionClass = "region";

    /// <summary>流段容器类名。</summary>
    public const string StreamItemClass = "stream-item";

    /// <summary>区域容器：<c>&lt;div class="region" data-name="{name}"&gt;inner&lt;/div&gt;</c>。</summary>
    public static string Region(string name, string? inner, MarkupDialect? dialect = null)
        => MarkupBlock.Render("div", RegionClass, RegionClass, name, inner, dialect);

    /// <summary>流段包裹：<c>&lt;div class="stream-item" data-key="{key}"&gt;inner&lt;/div&gt;</c>（段键 = 差分键）。</summary>
    public static string StreamItem(string key, string? inner, MarkupDialect? dialect = null)
    {
        var d = dialect ?? MarkupDialects.Html;
        var attrs = new MarkupAttrBatch(d).Class(StreamItemClass, StreamItemClass).Key(key);
        return MarkupTag.Element("div", attrs, UiMarkup.Trusted(inner), d);
    }

    /// <summary>具名行内标段：<c>&lt;span data-name="{name}"&gt;inner&lt;/span&gt;</c>。</summary>
    public static string LabelledSpan(string name, string? inner, MarkupDialect? dialect = null)
    {
        var d = dialect ?? MarkupDialects.Html;
        return MarkupTag.Element("span", new MarkupAttrBatch(d).Name(name), UiMarkup.Trusted(inner), d);
    }
}
