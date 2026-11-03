namespace Usharp.Syntax;

/// <summary>首词标记的边界策略（调用方显式声明，禁隐式差异）。</summary>
public enum UsharpMarkerBoundary
{
    /// <summary>标记后须跟空白；整行即标记 ⇒ 不命中。</summary>
    Whitespace,

    /// <summary>标记后须跟空白；整行即标记 ⇒ 命中（rest 为空）。</summary>
    WhitespaceExactLine,

    /// <summary>标记后须跟 <c>|</c> 或空白；整行即标记 ⇒ 命中。</summary>
    WhitespaceOrPipe,

    /// <summary>标记后须跟 <c>:</c> 或空白；整行即标记 ⇒ 命中。</summary>
    WhitespaceOrColon,

    /// <summary>标记后须跟空白，且正文非空；整行即标记 ⇒ 不命中（替代 <see cref="UsharpLineReader.TryHead"/>）。</summary>
    HeadNonEmpty,
}

/// <summary>布尔词法接受集（差异必须显式声明，禁止各处各写一套）。</summary>
public enum UsharpBoolLexicon
{
    /// <summary>仅 <c>true</c>/<c>false</c>。</summary>
    Strict,

    /// <summary>+ <c>1</c>/<c>0</c>。</summary>
    Numeric,

    /// <summary>+ <c>on</c>/<c>off</c>。</summary>
    OnOff,

    /// <summary>+ <c>yes</c>/<c>no</c> + <c>l1</c>/<c>l0</c>（历史声明面全集）。</summary>
    Extended,
}

/// <summary>
/// 声明续行词法内核（6 套私有词法器的唯一实现）：行遍历 / 首词标记 / 字段袋 / 列表切分 / 空值。
/// <para>
/// 全为纯函数；口径差异一律由调用方显式传参（边界策略 / 字段比较符 / 列表分隔符），
/// 禁止在声明类内再写一份等价实现。
/// </para>
/// </summary>
public static class UsharpLineReader
{
    /// <summary>空白即 null（否则去空白）。</summary>
    public static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>按分隔符切段（去空段、逐段去空白）；默认 <c>|</c>。</summary>
    public static IReadOnlyList<string> SplitParts(string line, char separator = '|')
        => line.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>有序 <c>k=v</c> 字段袋（跳过前 <paramref name="skip"/> 段；比较符由调用方声明）。</summary>
    public static Dictionary<string, string> ParseFieldBag(
        IReadOnlyList<string> parts, int skip = 0, StringComparer? comparer = null)
    {
        var map = new Dictionary<string, string>(comparer ?? StringComparer.OrdinalIgnoreCase);
        for (var i = skip; i < parts.Count; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) continue;
            map[parts[i][..eq].Trim()] = parts[i][(eq + 1)..].Trim();
        }
        return map;
    }

    /// <summary>取非空字段值（缺省/空串 ⇒ null）。</summary>
    public static string? Get(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    /// <summary>列表切分（去空去空白）；分隔符显式声明，缺省 <c>,</c>+<c>;</c>。</summary>
    public static IReadOnlyList<string> SplitList(string? raw, params char[] separators)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        if (separators.Length == 0) separators = new[] { ',', ';' };
        return raw.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>首词标记剥离（边界与「整行即标记」由策略决定）。</summary>
    public static bool TryMarker(string line, string marker, UsharpMarkerBoundary boundary, out string rest)
    {
        rest = string.Empty;
        if (!line.StartsWith(marker, StringComparison.Ordinal)) return false;

        if (line.Length == marker.Length)
            return boundary is UsharpMarkerBoundary.WhitespaceExactLine
                or UsharpMarkerBoundary.WhitespaceOrPipe
                or UsharpMarkerBoundary.WhitespaceOrColon;

        var next = line[marker.Length];
        var matched = boundary switch
        {
            UsharpMarkerBoundary.Whitespace => char.IsWhiteSpace(next),
            UsharpMarkerBoundary.WhitespaceExactLine => char.IsWhiteSpace(next),
            UsharpMarkerBoundary.HeadNonEmpty => char.IsWhiteSpace(next),
            UsharpMarkerBoundary.WhitespaceOrPipe => next == '|' || char.IsWhiteSpace(next),
            UsharpMarkerBoundary.WhitespaceOrColon => next == ':' || char.IsWhiteSpace(next),
            _ => false,
        };
        if (!matched) return false;

        rest = boundary switch
        {
            UsharpMarkerBoundary.WhitespaceOrPipe => line[marker.Length..].TrimStart(' ', '|').Trim(),
            UsharpMarkerBoundary.WhitespaceOrColon => line[marker.Length..].TrimStart(' ', ':').Trim(),
            _ => line[marker.Length..].Trim(),
        };
        return boundary != UsharpMarkerBoundary.HeadNonEmpty || rest.Length > 0;
    }

    /// <summary>布尔词法（接受集显式声明；失败 ⇒ false，不猜默认）。</summary>
    /// <param name="raw">原文。</param>
    /// <param name="lexicon">接受集。</param>
    /// <param name="value">解析结果。</param>
    public static bool TryBool(string? raw, UsharpBoolLexicon lexicon, out bool value)
    {
        value = false;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        if (lexicon == UsharpBoolLexicon.Strict) return false;

        if (text == "1") { value = true; return true; }
        if (text == "0") { value = false; return true; }
        if (lexicon == UsharpBoolLexicon.Numeric) return false;

        if (string.Equals(text, "on", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        if (lexicon == UsharpBoolLexicon.OnOff) return false;

        if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (string.Equals(text, "no", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        if (string.Equals(text, "l1", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (string.Equals(text, "l0", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        return false;
    }

    /// <summary>
    /// 列表切分；空白输入 ⇒ <c>null</c>（区分「未声明」与「声明为空」，D-24）。
    /// </summary>
    /// <param name="raw">原文。</param>
    /// <param name="separators">分隔符（缺省 <c>,</c>+<c>;</c>）。</param>
    public static IReadOnlyList<string>? SplitListOrNull(string? raw, params char[] separators)
        => string.IsNullOrWhiteSpace(raw) ? null : SplitList(raw, separators);

    /// <summary>首词 + 空白后正文（整行即标记 ⇒ true 且 rest 为空；正文须非空否则 false）。</summary>
    public static bool TryHead(string head, string marker, out string rest)
    {
        rest = string.Empty;
        if (!head.StartsWith(marker, StringComparison.Ordinal)) return false;
        if (head.Length == marker.Length) return true;
        if (!char.IsWhiteSpace(head[marker.Length])) return false;
        rest = head[marker.Length..].Trim();
        return rest.Length > 0;
    }

    /// <summary><c>&lt;族&gt;/&lt;名&gt;</c> 引用切分（无 <c>/</c> ⇒ 两段空串）。</summary>
    public static (string Family, string Name) SplitRef(string text)
    {
        var slash = text.IndexOf('/');
        return slash <= 0 ? (string.Empty, string.Empty)
            : (text[..slash].Trim(), text[(slash + 1)..].Trim());
    }

    /// <summary>按首个 <c>:</c> 切两段（与 <see cref="UsharpLanguage.SplitPair"/> 同一口径）。</summary>
    public static (string Left, string Right) SplitPair(string segment) => UsharpLanguage.SplitPair(segment);

    /// <summary>取最后一段（<c>a.b.c</c> ⇒ <c>c</c>；无 <c>.</c> 或尾随 <c>.</c> ⇒ 原值）。</summary>
    public static string Tail(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot >= 0 && dot < value.Length - 1 ? value[(dot + 1)..] : value;
    }
}
