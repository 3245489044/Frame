using System.Text.RegularExpressions;

namespace BackEndDriven.CommandExecution;

/// <summary>
/// 命令层日志副本截断（单一实现）。
/// 避免超大函数结果/控件文本（如会话历史 HTML，内含 L0 基础提示词）整段灌入控制台与持久化日志；
/// 真实值仍原样回写控件、参与断言判定，本类只作用于「写入日志与失败消息的副本」。
/// </summary>
public static class LogTextTruncation
{
    /// <summary>日志副本字符预算（超出即截断并标注截断计数）。</summary>
    public const int MaxChars = 400;

    /// <summary>截断任意结果对象：非字符串仅取 ToString（其天然短，无预算意义）。</summary>
    public static string? TruncateForLog(object? output)
        => output is string s ? TruncateForLog(s) : output?.ToString();

    /// <summary>截断文本副本。HTML 文档先压平（剥标签 + 压空白）再按预算截断，避免把整棵视图树撕成不可读碎片；
    /// 普通长文本保留「首 N 字符 + 截断计数」语义（兼容既有日志/断言）。</summary>
    public static string TruncateForLog(string text)
    {
        if (text.Length <= MaxChars) return text;

        var body = IsHtmlDocument(text) ? FlattenHtml(text) : text;
        if (body.Length <= MaxChars) return body;

        return body.Substring(0, MaxChars) + $"...(截断 {text.Length - MaxChars} 字符)";
    }

    /// <summary>断言场景的截断副本：同预算下保留「头 + 尾」双窗口。
    /// 断言失配的判据证据常在文本尾部（如末条回答/错误气泡），仅保留头部会把关键证据截掉；
    /// 故头部 200 + 尾部 200，中间以省略标注携带省略字数（预算与 TruncateForLog 同级）。</summary>
    public static string TruncateForAssert(string text)
    {
        if (text.Length <= MaxChars) return text;

        var body = IsHtmlDocument(text) ? FlattenHtml(text) : text;
        if (body.Length <= MaxChars) return body;

        var half = MaxChars / 2;
        var head = body.Substring(0, half);
        var tail = body.Substring(body.Length - half);
        return $"{head}…(中间省略 {body.Length - MaxChars} 字符)…{tail}";
    }

    /// <summary>判定字符串是否为 HTML 文档（忽略前导空白后的首字符为 '&lt;'）。</summary>
    private static bool IsHtmlDocument(string s)
    {
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) continue;
            return c == '<';
        }
        return false;
    }

    /// <summary>剥 HTML 标签并压连续空白，返回纯文本轮廓（可能短于预算）；全部压空时给可辨识占位。</summary>
    private static string FlattenHtml(string s)
    {
        var noTags = Regex.Replace(s, "<[^>]*>", " ");
        var collapsed = Regex.Replace(noTags, "\\s+", " ").Trim();
        return string.IsNullOrEmpty(collapsed) ? "[html 压平后为空，仅存视图结构]" : collapsed;
    }
}
