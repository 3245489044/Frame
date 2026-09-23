using System.Text.RegularExpressions;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;

namespace Usharp.Syntax;

/// <summary>空间种类（键型 = 无值域、不可比较；值型 = 有值域、可量化；词元型 = 有词元集、不可量化）。</summary>
public enum UsharpSpaceKind
{
    /// <summary>键型空间：坐标是名字（步骤键 / op 键），无值域。</summary>
    Keys,

    /// <summary>值型空间：坐标有值域（flag/ordinal/numeric），可量化。</summary>
    Values,

    /// <summary>词元型空间（<c>kind=terms</c>）：坐标带词元集（命名枚举）与象限归属，装配为判据读法。</summary>
    Terms,
}

/// <summary>空间的一个坐标轴（值型带值类与值域；键型只有名字；词元型带象限与词元集）。</summary>
public sealed record UsharpAxis(string Name, string? ValueKind = null, double? Min = null, double? Max = null)
{
    /// <summary>是否键型坐标（无值类）。</summary>
    public bool IsKey => ValueKind is null;

    /// <summary>象限归属（<c>zone=</c>；仅 <c>kind=terms</c> 空间；null = 未声明，闸A <c>US1101</c>）。</summary>
    public string? Zone { get; init; }

    /// <summary>词元集（仅 <c>kind=terms</c> 空间；空表 = 未声明词元，闸A <c>US1101</c>）。</summary>
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();
}

/// <summary>包含声明的一条：<c>contains=&lt;ParentSpaceId&gt;:&lt;EdgeType&gt;</c>（子空间 → 父空间 + 包含边类型）。</summary>
/// <param name="ParentSpaceId">父空间 Id（本空间是其坐标子集）。</param>
/// <param name="EdgeType">包含边类型（喂 <c>HierarchyContainment.ContainsEdgeType</c>；不可为空）。</param>
public sealed record UsharpContainmentDecl(string ParentSpaceId, string EdgeType)
{
    /// <summary>
    /// 解析 <c>&lt;Id&gt;:&lt;EdgeType&gt;</c> 列表（同一字面量形状复用：<c>[Space] contains=</c> 的第二分量 = 父空间，
    /// <c>[Flow] connects=</c> 的第二分量 = 上游流号）。按 <c>;</c> 分段（与 <c>[Obs] enum=</c> 同一约定：
    /// <c>|</c> 与 <c>,</c> 已被字段/列表占用）；只做「名 : 边类型」的首次切分，形状校验留给闸A。
    /// </summary>
    public static IReadOnlyList<UsharpContainmentDecl> ParseList(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return Array.Empty<UsharpContainmentDecl>();

        var items = new List<UsharpContainmentDecl>();
        foreach (var segment in UsharpLanguage.SplitSegments(field))
        {
            var (id, edgeType) = UsharpLanguage.SplitPair(segment);
            items.Add(new UsharpContainmentDecl(id, edgeType));
        }
        return items;
    }
}

/// <summary>[Space] Name: kind=keys|values 的语法视图（缩进块逐行声明坐标）。</summary>
public sealed record UsharpSpaceDecl(
    string SpaceId,
    UsharpSpaceKind Kind,
    IReadOnlyList<UsharpAxis> Axes,
    string SourceFile,
    int LineNumber)
{
    /// <summary>
    /// 划分归属声明（<c>contains=</c>；空表 = 顶层空间）。**加法式 init 属性**（不改位置参数）：
    /// 一条 = 一个父空间；同一声明多父层时按 <c>;</c> 分段逐条登记。
    /// </summary>
    public IReadOnlyList<UsharpContainmentDecl> Contains { get; init; } = Array.Empty<UsharpContainmentDecl>();

    /// <summary>
    /// <c>kind=</c> 字段原文（null = 缺省）——「在场但非法」据其报 <c>US0006</c>，不静默降级为值型空间。
    /// </summary>
    public string? KindRaw { get; init; }

    /// <summary>
    /// 缩进块内无法解析的坐标行原文（空表 = 全部解析成功）——据其报 <c>US0007</c>，禁止静默丢弃声明行。
    /// </summary>
    public IReadOnlyList<string> UnparsedLines { get; init; } = Array.Empty<string>();

    private static readonly Regex AxisRegex = new(
        $"^(?<name>{UsharpIdentifier.Pattern})\\s*(?::\\s*(?<kind>[A-Za-z]+))?\\s*(?:\\(\\s*(?<min>-?[\\d.]+)\\s*\\.\\.\\s*(?<max>-?[\\d.]+)\\s*\\))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 词元行文法（仅 <c>kind=terms</c>）：<c>&lt;轴&gt; | zone=&lt;象限&gt; | terms=&lt;t1&gt;,&lt;t2&gt;</c>
    /// —— 字段序无关、字段可省（在场但非法由闸A <c>US1101</c> 报，不静默丢弃声明行）。
    /// </summary>
    private static readonly Regex TermAxisRegex = new(
        $"^(?<name>{UsharpIdentifier.Pattern})\\s*(?<rest>(\\|.*)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>解析一条词元行（未识别字段 ⇒ false，交由调用方记入 <see cref="UsharpSpaceDecl.UnparsedLines"/>）。</summary>
    private static bool TryParseTermAxis(string line, out UsharpAxis axis)
    {
        axis = null!;
        var m = TermAxisRegex.Match(line);
        if (!m.Success) return false;

        string? zone = null;
        var terms = new List<string>();
        var rest = m.Groups["rest"].Value ?? string.Empty;
        foreach (var segment in rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // `k=v` 切分单源 = 解析器字段词法（本读法不接受散文段：无 `=` 即形状非法，不静默丢弃）。
            var kv = FieldParser.TryParsePart(segment);
            if (kv is null) return false;

            var (key, value) = (kv.Value.Key, kv.Value.Value);
            if (string.Equals(key, UsharpLanguage.FieldZone, StringComparison.OrdinalIgnoreCase))
            {
                zone = string.IsNullOrWhiteSpace(value) ? null : value;
                continue;
            }
            if (string.Equals(key, UsharpLanguage.FieldTerms, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var term in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    terms.Add(term);
                continue;
            }
            return false;   // 未识别字段：不静默丢弃（既有 US0007 通道报出）
        }

        axis = new UsharpAxis(m.Groups["name"].Value) { Zone = zone, Terms = terms };
        return true;
    }

    public static UsharpSpaceDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var kindRaw = d.Field(UsharpLanguage.FieldKind)?.Trim();
        var kind = string.Equals(kindRaw, UsharpLanguage.KindKeys, StringComparison.OrdinalIgnoreCase) ? UsharpSpaceKind.Keys
            : string.Equals(kindRaw, UsharpLanguage.KindTerms, StringComparison.OrdinalIgnoreCase) ? UsharpSpaceKind.Terms
            : UsharpSpaceKind.Values;

        var axes = new List<UsharpAxis>();
        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (kind == UsharpSpaceKind.Terms)
            {
                // 词元行（独立文法，不并入 keys/values 的 AxisRegex ⇒ 既有两读法零回归）。
                if (!TryParseTermAxis(line, out var termAxis))
                {
                    unparsed.Add(line);
                    continue;
                }
                axes.Add(termAxis);
                continue;
            }

            var m = AxisRegex.Match(line);
            if (!m.Success)
            {
                unparsed.Add(line);
                continue;
            }
            var valueKind = kind == UsharpSpaceKind.Keys ? null
                : (m.Groups["kind"].Success ? m.Groups["kind"].Value.ToLowerInvariant() : "numeric");
            double? min = m.Groups["min"].Success && double.TryParse(m.Groups["min"].Value, out var mn) ? mn : null;
            double? max = m.Groups["max"].Success && double.TryParse(m.Groups["max"].Value, out var mx) ? mx : null;
            axes.Add(new UsharpAxis(m.Groups["name"].Value, valueKind, min, max));
        }
        var contains = UsharpContainmentDecl.ParseList(d.Field(UsharpLanguage.FieldContains));

        return new UsharpSpaceDecl(d.Name, kind, axes, d.SourceFile, d.LineNumber)
        {
            Contains = contains,
            KindRaw = string.IsNullOrWhiteSpace(kindRaw) ? null : kindRaw,
            UnparsedLines = unparsed,
        };
    }
}
