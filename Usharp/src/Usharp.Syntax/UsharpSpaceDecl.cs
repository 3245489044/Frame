using System.Text.RegularExpressions;
using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>空间种类（键型 = 无值域、不可比较；值型 = 有值域、可量化）。</summary>
public enum UsharpSpaceKind
{
    /// <summary>键型空间：坐标是名字（步骤键 / op 键），无值域。</summary>
    Keys,

    /// <summary>值型空间：坐标有值域（flag/ordinal/numeric），可量化。</summary>
    Values,
}

/// <summary>空间的一个坐标轴（值型带值类与值域；键型只有名字）。</summary>
public sealed record UsharpAxis(string Name, string? ValueKind = null, double? Min = null, double? Max = null)
{
    /// <summary>是否键型坐标（无值类）。</summary>
    public bool IsKey => ValueKind is null;
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

    public static UsharpSpaceDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var kindRaw = d.Field(UsharpLanguage.FieldKind)?.Trim();
        var kind = string.Equals(kindRaw, UsharpLanguage.KindKeys,
            StringComparison.OrdinalIgnoreCase) ? UsharpSpaceKind.Keys : UsharpSpaceKind.Values;

        var axes = new List<UsharpAxis>();
        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

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
