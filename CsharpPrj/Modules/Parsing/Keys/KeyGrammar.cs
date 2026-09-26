namespace Modules.Parsing.Keys;

/// <summary>
/// 键文法（**键空间单源**）：族前缀 + 模板（当前形态 + 存量兼容形态）+ 序数占位 + 键序项。
/// <para>存在理由（SingleImplementation / 键面收敛）：同一键空间此前常被三份表达各自持有
/// （声明文件模板 / C# 常量 / 手写切片与正则），任一处改动不报错即静默漂移。本类把
/// 「构造 / 解析 / 族判据 / 排序」四维收敛为一份可实现值，调用方只注册文法、不再拼串。</para>
/// <para>纪律：内核不含任何具体键名（领域无关）；<see cref="FamilyPrefix"/> 未给出时视为「无族前缀」，
/// 仅参与无前缀文法的消歧集合（注册表见 <see cref="KeyGrammarRegistry"/>）。</para>
/// </summary>
public sealed class KeyGrammar
{
    private readonly KeyTemplate _primary;
    private readonly KeyTemplate[] _legacy;

    private KeyGrammar(
        string grammarId,
        string familyPrefix,
        string template,
        IReadOnlyCollection<string>? sequenceKeys,
        IReadOnlyCollection<string>? legacyTemplates,
        IReadOnlyCollection<string>? orderKeys)
    {
        GrammarId = grammarId;
        FamilyPrefix = familyPrefix ?? string.Empty;
        _primary = new KeyTemplate(template, grammarId);
        _legacy = (legacyTemplates ?? Array.Empty<string>())
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(text => new KeyTemplate(text, grammarId))
            .ToArray();

        SequenceKeys = RequireSubset(
            sequenceKeys ?? Array.Empty<string>(),
            _primary.PlaceholderNames,
            grammarId,
            "序数占位未出现在模板中");

        OrderKeys = orderKeys is null
            ? _primary.PlaceholderNames
            : RequireSubset(orderKeys, _primary.PlaceholderNames, grammarId, "键序项未出现在模板中");

        Comparer = KeyOrdinal.ForGrammar(this);
    }

    /// <summary>文法 id（注册表主键；同 id 即同一键空间，禁重复注册）。</summary>
    public string GrammarId { get; }

    /// <summary>族前缀（族判据用；空串 = 无族前缀）。</summary>
    public string FamilyPrefix { get; }

    /// <summary>当前模板原文。</summary>
    public string Template => _primary.Text;

    /// <summary>全部段（序 = 模板序）。</summary>
    public IReadOnlyList<KeySegment> Segments => _primary.Segments;

    /// <summary>全部占位符名（序 = 模板序）。</summary>
    public IReadOnlyList<string> ValueKeys => _primary.PlaceholderNames;

    /// <summary>序数占位（由序数权威单源分配；缺即序数丢失）。</summary>
    public IReadOnlyList<string> SequenceKeys { get; }

    /// <summary>键序项（比较顺序；缺省 = 占位符序）。</summary>
    public IReadOnlyList<string> OrderKeys { get; }

    /// <summary>存量兼容模板（只读；反解容忍，构造一律用当前模板）。</summary>
    public IReadOnlyList<string> LegacyTemplates => _legacy.Select(static t => t.Text).ToArray();

    /// <summary>是否含可缺省区（尾部可选段）。</summary>
    public bool HasOptionalSegments => _primary.RegionCount > 0;

    /// <summary>本文法键序比较器（<c>OrderBy(k =&gt; k, grammar.Comparer)</c>）。</summary>
    public IComparer<string> Comparer { get; }

    /// <summary>建文法（定义错误即抛 <see cref="KeyGrammarDefinitionException"/>，不静默降级）。</summary>
    /// <param name="grammarId">文法 id（非空，注册表主键）。</param>
    /// <param name="template">键模板（迷你文法见 <see cref="KeyTemplate"/>）。</param>
    /// <param name="familyPrefix">族前缀（空串 = 无族前缀）。</param>
    /// <param name="sequenceKeys">序数占位名（须出现在模板中）。</param>
    /// <param name="legacyTemplates">存量兼容模板（只读容忍；构造一律用当前模板）。</param>
    /// <param name="orderKeys">键序项（缺省 = 占位符序）。</param>
    public static KeyGrammar Create(
        string grammarId,
        string template,
        string familyPrefix = "",
        IReadOnlyCollection<string>? sequenceKeys = null,
        IReadOnlyCollection<string>? legacyTemplates = null,
        IReadOnlyCollection<string>? orderKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grammarId);
        return new KeyGrammar(grammarId, familyPrefix, template, sequenceKeys, legacyTemplates, orderKeys);
    }

    /// <summary>构造键：必填缺值 / 可选区半填 ⇒ false + <paramref name="missing"/>（<b>不产出半成品键</b>）。</summary>
    public bool TryBuild(
        IReadOnlyDictionary<string, string> values,
        out string key,
        out IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(values);
        return _primary.TryEvaluate(values, renderPartial: false, out key, out missing);
    }

    /// <summary>构造键（缺值即抛；键面无半成品，不静默降级）。</summary>
    public string Build(IReadOnlyDictionary<string, string> values)
        => TryBuild(values, out var key, out var missing)
            ? key
            : throw new InvalidOperationException($"键文法 '{GrammarId}' 求值缺值: {string.Join(",", missing)}");

    /// <summary>按位序构造（位序 = <see cref="ValueKeys"/> 顺序）：无字典分配，供高频构造点复用。</summary>
    public bool TryBuildByPosition(
        IReadOnlyList<string?> orderedValues,
        out string key,
        out IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(orderedValues);
        return _primary.TryEvaluate(orderedValues, renderPartial: false, out key, out missing);
    }

    /// <summary>按位序构造（缺值即抛；位序 = <see cref="ValueKeys"/> 顺序）。</summary>
    public string BuildByPosition(params string?[] orderedValues)
        => TryBuildByPosition(orderedValues, out var key, out var missing)
            ? key
            : throw new InvalidOperationException($"键文法 '{GrammarId}' 求值缺值: {string.Join(",", missing)}");

    /// <summary>
    /// 宽松构造（**迁移兼容契约**）：缺值段以空串代入，恒返回键（不抛、不判失败）——
    /// 与各域既有「拼串」实现逐字一致（保留历史键族的既有退化行为，如空会话名 → <c>session..r3.msg.7</c>）。
    /// <para>仅用于既有键族的**逐字对拍**迁移；**新代码** 一律用 <see cref="TryBuild"/>（严格：失败即空键，不产出半成品键）。</para>
    /// </summary>
    public string BuildLenient(params string?[] orderedValues)
    {
        _primary.TryEvaluate(orderedValues, renderPartial: true, out var key, out _);
        return key;
    }

    /// <summary>紧邻指定占位符<b>左侧</b>的字面量段（中缀/分隔符；无占位符或无紧邻字面量 ⇒ 空串）。</summary>
    public string LiteralBefore(string placeholder)
    {
        for (var index = 1; index < Segments.Count; index++)
        {
            var segment = Segments[index];
            if (segment.Kind == KeySegmentKind.Literal ||
                !string.Equals(segment.Text, placeholder, StringComparison.Ordinal))
            {
                continue;
            }

            var previous = Segments[index - 1];
            return previous.Kind == KeySegmentKind.Literal ? previous.Text : string.Empty;
        }

        return string.Empty;
    }

    /// <summary>族判据（唯一实现；替代各处散落的 <c>StartsWith("xxx.")</c>）。</summary>
    public bool IsFamily(string? key)
        => !string.IsNullOrEmpty(key) &&
           (FamilyPrefix.Length == 0 || key.StartsWith(FamilyPrefix, StringComparison.Ordinal));

    /// <summary>按当前模板反解（存量形态不命中 ⇒ false）。</summary>
    public bool TryParse(string? key, out KeyValues values)
    {
        if (string.IsNullOrEmpty(key))
        {
            values = KeyValues.Empty;
            return false;
        }

        return _primary.TryParse(key, out values);
    }

    /// <summary>按当前 + 存量模板反解（<paramref name="matchedTemplate"/> 给出命中形态，便于迁移可见化）。</summary>
    public bool TryParseAny(string? key, out KeyValues values, out string matchedTemplate)
    {
        if (TryParse(key, out values))
        {
            matchedTemplate = Template;
            return true;
        }

        foreach (var legacy in _legacy)
        {
            if (legacy.TryParse(key!, out values))
            {
                matchedTemplate = legacy.Text;
                return true;
            }
        }

        matchedTemplate = string.Empty;
        return false;
    }

    /// <summary>按当前 + 存量模板反解（不关心命中形态；等价 <c>TryParseAny(key, out values, out _)</c>）。</summary>
    public bool TryParseAny(string? key, out KeyValues values) => TryParseAny(key, out values, out _);

    /// <summary>该占位是否数值段（键序按数值比较；缺省形状 Text 视为序数段）。</summary>
    public bool IsNumeric(string key)
    {
        foreach (var segment in Segments)
        {
            if (segment.Kind != KeySegmentKind.Literal &&
                string.Equals(segment.Text, key, StringComparison.Ordinal))
            {
                return segment.Shape is KeyValueShape.Number or KeyValueShape.Digits;
            }
        }

        return false;
    }

    /// <summary>当前模板的锚定反解正则（诊断/机检用）。</summary>
    public string ToRegexPattern() => _primary.Pattern.ToString();

    /// <summary>
    /// 与另一文法是否「可区分」（按形状正则比较：组名不同不算可区分；同为
    /// <c>{a}.{b}</c> / <c>{b}.{a}</c> 属同一键空间，判为不可区分）。
    /// </summary>
    public bool IsDistinguishableFrom(KeyGrammar other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return !string.Equals(
            _primary.ShapePattern.ToString(),
            other._primary.ShapePattern.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 形状不符值名清单（构造侧**不**据此拒绝 —— 保留业务侧既有校验语义；供需要严格形状的调用方显式启用）。
    /// </summary>
    public IReadOnlyList<string> ShapeMismatches(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var mismatched = new List<string>();

        foreach (var segment in Segments)
        {
            if (segment.Kind == KeySegmentKind.Literal || segment.Shape == KeyValueShape.Text)
            {
                continue;
            }

            if (!values.TryGetValue(segment.Text, out var value) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!MatchesShape(value, segment.Shape))
            {
                mismatched.Add(segment.Text);
            }
        }

        return mismatched;
    }

    private static bool MatchesShape(string value, KeyValueShape shape) => shape switch
    {
        KeyValueShape.Id => value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'),
        KeyValueShape.Number => value.Length > 0 && value[0] != '0' && value.All(char.IsAsciiDigit),
        KeyValueShape.Digits => value.Length > 0 && value.All(char.IsAsciiDigit),
        _ => true,
    };

    private static IReadOnlyList<string> RequireSubset(
        IReadOnlyCollection<string> keys,
        IReadOnlyList<string> placeholders,
        string grammarId,
        string reason)
    {
        var result = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!placeholders.Contains(key, StringComparer.Ordinal))
            {
                throw new KeyGrammarDefinitionException($"{reason}: {key}", grammarId);
            }

            result.Add(key);
        }

        return result;
    }
}
