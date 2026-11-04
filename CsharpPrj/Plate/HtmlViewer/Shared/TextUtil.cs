#nullable enable

using Modules.Parsing.Markup;

namespace UiBackend.Html;

/// <summary>HTML 后端共用文本工具（程序集内共享，避免各桥各持一份重复实现）。</summary>
internal static class TextUtil
{
    /// <summary>超长文本截断（用于诊断日志，避免整页负载写满日志）。</summary>
    public static string Truncate(string s, int max = 200)
        => s.Length <= max ? s : s[..max] + $"…(截断 {s.Length})";

    /// <summary>CSS 类名兜底（空/空白 ⇒ fallback）—— 单源 = <see cref="MarkupClass.Fallback"/>。</summary>
    public static string CssClass(string? value, string fallback) => MarkupClass.Fallback(value, fallback);

    /// <summary>空/空白判定（与 <see cref="CssClass"/> 同口径）。</summary>
    public static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
