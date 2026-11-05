using System;

namespace Modules.Parsing.Keys;

/// <summary>
/// 键/文本边界归一（通用件）：trim、空白→null、必填断言；读写两路共用同一口径。
/// </summary>
public static class KeyText
{
    /// <summary>归一化：trim；空/空白 ⇒ null（禁返回空串冒充「有键」）。</summary>
    public static string? Normalize(string? key)
    {
        var text = key?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>归一化并断言非空（空白即抛，异常类型由调用方语境决定）。</summary>
    public static string Require(string? key, string paramName)
        => Normalize(key) ?? throw new ArgumentException("键不能为空（空白等价于缺失）。", paramName);

    /// <summary>归一化；空 ⇒ 空串（仅用于必须产出字符串的诊断/日志面）。</summary>
    public static string NormalizeOrEmpty(string? key) => Normalize(key) ?? string.Empty;

    /// <summary>是否空白（统一判定，禁调用点各写 IsNullOrWhiteSpace）。</summary>
    public static bool IsBlank(string? key) => Normalize(key) is null;

    /// <summary>两个键是否等价（归一化后序数比较）。</summary>
    public static bool Same(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}
