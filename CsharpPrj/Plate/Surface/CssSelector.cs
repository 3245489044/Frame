#nullable enable

namespace UiBackend;

/// <summary>
/// CSS 选择器文本单点（**唯一实现**）：类选择器 / 属性选择器不再由各载体手拼。
/// <para>只做文本形态，不做匹配语义；属性值按双引号包裹（槽名均为标识符，不引入引号转义）。</para>
/// </summary>
public static class CssSelector
{
    /// <summary>类选择器：<c>.{className}</c>。</summary>
    public static string Class(string className) => "." + className;

    /// <summary>属性选择器：<c>[name="value"]</c>。</summary>
    public static string Attr(string name, string? value) => $"[{name}=\"{value}\"]";
}
