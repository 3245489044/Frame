#nullable enable

namespace UiBackend.Html;

/// <summary>
/// 点击结果分型（唯一词表）——桥、真实点击执行器、脚本判据共用一处，
/// 消除「各层各一套失败词」造成的跨层对账只能靠人读文本。
/// </summary>
public enum ClickOutcome
{
    Ok,
    NotFound,
    NotVisible,
    OffViewport,
    Occluded,
    NotEffective,
    DpiMismatch,
    InputBlocked,
    ParseError,
    Unsupported,
    FunctionFault,
    Split,
    Stale,
    Other
}

/// <summary>
/// 分型 ↔ 文本前缀（唯一映射）。前缀保持既有词汇（OK/NOT_FOUND/NOT_VISIBLE/NOT_EFFECTIVE/PARSE_ERROR），
/// 使存量断言与日志检索零回归；新增前缀只在此处登记。
/// </summary>
public static class ClickOutcomes
{
    public static string Prefix(ClickOutcome outcome) => outcome switch
    {
        ClickOutcome.Ok => "OK",
        ClickOutcome.NotFound => "NOT_FOUND",
        ClickOutcome.NotVisible => "NOT_VISIBLE",
        ClickOutcome.OffViewport => "OFF_VIEWPORT",
        ClickOutcome.Occluded => "OCCLUDED",
        ClickOutcome.NotEffective => "NOT_EFFECTIVE",
        ClickOutcome.DpiMismatch => "DPI_MISMATCH",
        ClickOutcome.InputBlocked => "INPUT_BLOCKED",
        ClickOutcome.ParseError => "PARSE_ERROR",
        ClickOutcome.Unsupported => "UNSUPPORTED",
        ClickOutcome.FunctionFault => "FUNCTION_FAULT",
        ClickOutcome.Split => "SPLIT",
        ClickOutcome.Stale => "STALE",
        _ => "OTHER"
    };

    /// <summary>从结果文本解析分型（取首个分隔符前的前缀；未知前缀=Other，禁静默当成功）。</summary>
    public static ClickOutcome Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ClickOutcome.Other;
        var trimmed = text.Trim();
        if (trimmed.Length > 1 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed.Substring(1, trimmed.Length - 2);

        var end = 0;
        while (end < trimmed.Length && trimmed[end] != '|' && trimmed[end] != ';' && !char.IsWhiteSpace(trimmed[end]))
            end++;
        var token = trimmed.Substring(0, end).ToUpperInvariant();

        return token switch
        {
            "OK" => ClickOutcome.Ok,
            "NOT_FOUND" => ClickOutcome.NotFound,
            "NOT_VISIBLE" => ClickOutcome.NotVisible,
            "OFF_VIEWPORT" => ClickOutcome.OffViewport,
            "OCCLUDED" => ClickOutcome.Occluded,
            "NOT_EFFECTIVE" => ClickOutcome.NotEffective,
            "DPI_MISMATCH" => ClickOutcome.DpiMismatch,
            "INPUT_BLOCKED" => ClickOutcome.InputBlocked,
            "PARSE_ERROR" => ClickOutcome.ParseError,
            "UNSUPPORTED" => ClickOutcome.Unsupported,
            "FUNCTION_FAULT" => ClickOutcome.FunctionFault,
            "SPLIT" => ClickOutcome.Split,
            "STALE" => ClickOutcome.Stale,
            _ => ClickOutcome.Other
        };
    }

    public static bool IsFailure(ClickOutcome outcome) => outcome != ClickOutcome.Ok;

    /// <summary>探测脚本直接返回终态文本（自带诊断后缀）——无需再解析结构化负载。</summary>
    public static bool IsTerminalText(string? text)
        => Parse(text) is ClickOutcome.NotFound or ClickOutcome.NotVisible or ClickOutcome.ParseError;
}
