using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Modules.Parsing.Keys;

/// <summary>
/// 键模板（**唯一词法/求值/反解实现**）：段序列 + 可缺省区 + 值形状 → 正向求值（值 → 键）与反向反解（键 → 值）。
/// <para>
/// 模板迷你文法（段序即语义，位置即端口）：
/// <code>
/// Template       ::= Item*
/// Item           ::= Placeholder | Literal | OptionalRegion
/// Placeholder    ::= "{" NAME (":" SHAPE)? "}"      // SHAPE ∈ text|id|number|digits（缺省 text）
/// OptionalRegion ::= "[" Item* "]"                   // 区内段可整体缺席；只允许**嵌套**（不允许同级可选区）
/// Literal        ::= 任意非 {}/[]/\\ 字符；"\\" 后一字符按字面量处理
/// </code>
/// 嵌套语义 = 前区在场方可出现的尾段（正好表达 FactId 的 <c>#R&lt;n&gt;/step#attempt/iter/call</c> 层级尾部）。
/// </para>
/// <para>
/// 纪律：占位符名唯一；序数段须在模板中显式出现（缺序数占位即序数丢失）；可选区连续嵌套升序。
/// 反解正则锚定且占位符非贪婪 —— 与既有 <c>KeyTemplateEvaluator</c> 语义逐字一致（迁移期以逐字对拍验收）。
/// </para>
/// </summary>
internal sealed class KeyTemplate
{
    private static readonly Regex PlaceholderRegex =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)(?::([a-z]+))?\}", RegexOptions.Compiled);

    /// <summary>单次匹配上限：异常键不得拖死宿主（超时 ⇒ 反解失败，不静默当命中）。</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>正则缓存（键 → 值逐键调用，禁每次重编译）；检索键 = 生成的模式串。</summary>
    private static readonly ConcurrentDictionary<string, Regex> PatternCache = new(StringComparer.Ordinal);

    private readonly Lazy<Regex> _pattern;
    private readonly Lazy<Regex> _shapePattern;
    private readonly Dictionary<string, int> _indexByName;

    internal KeyTemplate(string text, string grammarId)
    {
        Text = text ?? string.Empty;
        if (Text.Length == 0)
        {
            throw new KeyGrammarDefinitionException("模板为空", grammarId);
        }

        var segments = new List<KeySegment>();
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var literal = new StringBuilder();
        var region = 0;
        var maxRegion = 0;
        var regionHasSegment = false;
        var index = 0;

        while (index < Text.Length)
        {
            var current = Text[index];

            if (current == '\\' && index + 1 < Text.Length)
            {
                literal.Append(Text[index + 1]);
                index += 2;
                continue;
            }

            if (current == '[')
            {
                FlushLiteral(segments, literal, region, ref regionHasSegment);
                region++;
                if (region <= maxRegion)
                {
                    throw new KeyGrammarDefinitionException("可选区不连续（只允许嵌套，不允许同级可选区）", grammarId);
                }

                maxRegion = region;
                regionHasSegment = false;
                index++;
                continue;
            }

            if (current == ']')
            {
                FlushLiteral(segments, literal, region, ref regionHasSegment);
                if (region == 0)
                {
                    throw new KeyGrammarDefinitionException("游离 ']'（无对应 '['）", grammarId);
                }

                if (!regionHasSegment)
                {
                    throw new KeyGrammarDefinitionException("可选区为空", grammarId);
                }

                region--;
                index++;
                continue;
            }

            if (current == '{')
            {
                var match = PlaceholderRegex.Match(Text, index);
                if (!match.Success || match.Index != index)
                {
                    throw new KeyGrammarDefinitionException($"占位符非法（偏移 {index}）", grammarId);
                }

                FlushLiteral(segments, literal, region, ref regionHasSegment);
                var name = match.Groups[1].Value;
                if (!seen.Add(name))
                {
                    throw new KeyGrammarDefinitionException($"占位符重复: {name}", grammarId);
                }

                var shape = ParseShape(match.Groups[2].Success ? match.Groups[2].Value : null, grammarId);
                segments.Add(new KeySegment(KeySegmentKind.Value, name, shape, region));
                names.Add(name);
                regionHasSegment = true;
                index += match.Length;
                continue;
            }

            if (current == '}')
            {
                throw new KeyGrammarDefinitionException($"游离 '}}'（偏移 {index}）", grammarId);
            }

            literal.Append(current);
            index++;

        }

        FlushLiteral(segments, literal, region, ref regionHasSegment);

        if (region != 0)
        {
            throw new KeyGrammarDefinitionException("可选区未闭合（缺 ']'）", grammarId);
        }

        if (names.Count == 0)
        {
            throw new KeyGrammarDefinitionException("模板不含占位符", grammarId);
        }

        Segments = segments;
        RegionCount = maxRegion;
        PlaceholderNames = names;
        _indexByName = new Dictionary<string, int>(names.Count, StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
        {
            _indexByName[names[i]] = i;
        }

        var patternText = BuildPattern(segments, named: true);
        var shapeText = BuildPattern(segments, named: false);
        _pattern = new Lazy<Regex>(() => PatternCache.GetOrAdd(
            patternText,
            text => new Regex(text, RegexOptions.Singleline, MatchTimeout)));
        _shapePattern = new Lazy<Regex>(() => PatternCache.GetOrAdd(
            shapeText,
            text => new Regex(text, RegexOptions.Singleline, MatchTimeout)));
    }

    /// <summary>模板原文（含 <c>{}</c> / <c>[]</c> 语法）。</summary>
    internal string Text { get; }

    /// <summary>全部段（含字面量段与可选区内段，序 = 模板序）。</summary>
    internal IReadOnlyList<KeySegment> Segments { get; }

    /// <summary>可缺省区数（0 = 无可选区）。</summary>
    internal int RegionCount { get; }

    /// <summary>全部占位符名（序 = 模板序）。</summary>
    internal IReadOnlyList<string> PlaceholderNames { get; }

    /// <summary>正/反解共用的锚定正则（命名捕获）。</summary>
    internal Regex Pattern => _pattern.Value;

    /// <summary>形状正则（占位符匿名化）：用于「同域可区分性」判定与去重（组名不参与判定）。</summary>
    internal Regex ShapePattern => _shapePattern.Value;

    /// <summary>正向求值（按名）：缺必填值 / 可选区半填 ⇒ false + <paramref name="missing"/>。</summary>
    /// <param name="values">占位符名 → 值。</param>
    /// <param name="renderPartial">
    /// true = **模板级兼容契约**：失败时仍给出「缺值段以空串代入」的半成品键（既有 <c>KeyTemplateEvaluator</c> 语义，
    /// 调用方须判返回值）；
    /// false = **文法级严格契约**：失败时 <paramref name="key"/> 为空串（不产出半成品键 —— 半成品键是静默漂移源）。
    /// </param>
    /// <param name="key">out：键（失败且非兼容模式 ⇒ 空串）。</param>
    /// <param name="missing">out：缺值占位符（序 = 段序）。</param>
    internal bool TryEvaluate(
        IReadOnlyDictionary<string, string> values,
        bool renderPartial,
        out string key,
        out IReadOnlyList<string> missing)
    {
        var ordered = new string?[PlaceholderNames.Count];
        for (var index = 0; index < PlaceholderNames.Count; index++)
        {
            ordered[index] = values.TryGetValue(PlaceholderNames[index], out var value) ? value : null;
        }

        return TryEvaluate(ordered, renderPartial, out key, out missing);
    }

    /// <summary>
    /// 正向求值（按位序 = <see cref="PlaceholderNames"/> 顺序；无字典分配，供高频构造点复用）。
    /// </summary>
    internal bool TryEvaluate(
        IReadOnlyList<string?> orderedValues,
        bool renderPartial,
        out string key,
        out IReadOnlyList<string> missing)
    {
        var absent = new List<string>();
        var builder = new StringBuilder(Text.Length * 2);

        for (var region = 0; region <= RegionCount; region++)
        {
            if (region > 0 && RegionIsEmpty(orderedValues, region))
            {
                continue;
            }

            foreach (var segment in Segments)
            {
                if (segment.Region != region)
                {
                    continue;
                }

                if (segment.Kind == KeySegmentKind.Literal)
                {
                    builder.Append(segment.Text);
                    continue;
                }

                var index = _indexByName[segment.Text];
                var value = index < orderedValues.Count ? orderedValues[index] : null;
                if (!string.IsNullOrEmpty(value))
                {
                    builder.Append(value);
                }
                else
                {
                    absent.Add(segment.Text);
                }
            }
        }

        missing = absent;
        key = absent.Count == 0 || renderPartial ? builder.ToString() : string.Empty;
        return absent.Count == 0;
    }

    /// <summary>反向反解：键与该模板不同形 ⇒ false + 空值袋（不猜）。</summary>
    internal bool TryParse(string key, out KeyValues values)
    {
        Match match;
        try
        {
            match = Pattern.Match(key);
        }
        catch (RegexMatchTimeoutException)
        {
            values = KeyValues.Empty;
            return false;
        }

        if (!match.Success)
        {
            values = KeyValues.Empty;
            return false;
        }

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in PlaceholderNames)
        {
            var group = match.Groups[name];
            parsed[name] = group.Success ? group.Value : string.Empty;
            if (group.Success)
            {
                present.Add(name);
            }
        }

        values = new KeyValues(parsed, present);
        return true;
    }

    /// <summary>区内占位符是否全空（⇒ 该区整体缺席）；半填由正向求值报 missing。</summary>
    private bool RegionIsEmpty(IReadOnlyList<string?> orderedValues, int region)
    {
        foreach (var segment in Segments)
        {
            if (segment.Region != region || segment.Kind == KeySegmentKind.Literal)
            {
                continue;
            }

            var index = _indexByName[segment.Text];
            if (index < orderedValues.Count && !string.IsNullOrEmpty(orderedValues[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>字面量缓冲 → 段（非空才落段；同时标记本区已有段 ⇒ 空区可判）。</summary>
    private static void FlushLiteral(
        List<KeySegment> segments,
        StringBuilder literal,
        int region,
        ref bool regionHasSegment)
    {
        if (literal.Length == 0)
        {
            return;
        }

        segments.Add(new KeySegment(KeySegmentKind.Literal, literal.ToString(), KeyValueShape.Text, region));
        regionHasSegment = true;
        literal.Clear();
    }

    private static KeyValueShape ParseShape(string? token, string grammarId) => token switch
    {
        null or "text" => KeyValueShape.Text,
        "id" => KeyValueShape.Id,
        "number" => KeyValueShape.Number,
        "digits" => KeyValueShape.Digits,
        "enum" => KeyValueShape.Enum,
        "path" => KeyValueShape.Path,
        _ => throw new KeyGrammarDefinitionException($"未知值形状: {token}", grammarId),
    };

    /// <summary>
    /// 段序列 → 锚定正则：可选区按**嵌套**展开（区 k 闭合于区 k-1 内），占位符按形状给定捕获体。
    /// <paramref name="named"/> = false 时组名匿名化（形状正则，供可区分性判定）。
    /// </summary>
    private static string BuildPattern(IReadOnlyList<KeySegment> segments, bool named)
    {
        var builder = new StringBuilder("^");
        var region = 0;

        foreach (var segment in segments)
        {
            while (region < segment.Region)
            {
                builder.Append("(?:");
                region++;
            }

            if (segment.Kind == KeySegmentKind.Literal)
            {
                builder.Append(Regex.Escape(segment.Text));
                continue;
            }

            var body = ShapePatternOf(segment.Shape);
            builder.Append(named ? $"(?<{segment.Text}>{body})" : $"(?:{body})");
        }

        while (region > 0)
        {
            builder.Append(")?");
            region--;
        }

        return builder.Append('$').ToString();
    }

    private static string ShapePatternOf(KeyValueShape shape) => shape switch
    {
        KeyValueShape.Id => @"[A-Za-z0-9_.\-]+?",
        KeyValueShape.Number => "[1-9][0-9]*",
        KeyValueShape.Digits => "[0-9]+",
        KeyValueShape.Enum => @"[A-Za-z_][A-Za-z0-9_]*",
        KeyValueShape.Path => @"(?:[^./\\]+|\\.)+?",
        _ => ".+?",
    };
}
