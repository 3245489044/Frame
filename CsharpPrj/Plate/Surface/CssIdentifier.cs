#nullable enable

using System.Text;

namespace UiBackend;

/// <summary>
/// CSS 标识符清洗（**唯一实现**）：保留字母/数字/<c>-</c>/<c>_</c>，其余字符滤除，可带固定前缀。
/// <para>只做字符过滤（不做转义/引号）；多类名（<c>"a b"</c>）不属于标识符，调用方先分段。</para>
/// </summary>
public static class CssIdentifier
{
    /// <summary>清洗为合法 CSS 标识符片段（空/全非法 ⇒ 只剩前缀）。</summary>
    /// <param name="raw">原始 id/类名片段。</param>
    /// <param name="prefix">恒保留的前缀（如 <c>material-</c>）。</param>
    public static string Sanitize(string? raw, string prefix = "")
    {
        var builder = new StringBuilder(prefix.Length + (raw?.Length ?? 0));
        builder.Append(prefix);
        foreach (var ch in raw ?? string.Empty)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_') builder.Append(ch);
        }

        return builder.ToString();
    }
}
