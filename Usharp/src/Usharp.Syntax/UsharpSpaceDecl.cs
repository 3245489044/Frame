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

/// <summary>[Space] Name: kind=keys|values 的语法视图（缩进块逐行声明坐标）。</summary>
public sealed record UsharpSpaceDecl(
    string SpaceId,
    UsharpSpaceKind Kind,
    IReadOnlyList<UsharpAxis> Axes,
    string SourceFile,
    int LineNumber)
{
    private static readonly Regex AxisRegex = new(
        @"^(?<name>[A-Za-z_][A-Za-z0-9_\-]*)\s*(?::\s*(?<kind>[A-Za-z]+))?\s*(?:\(\s*(?<min>-?[\d.]+)\s*\.\.\s*(?<max>-?[\d.]+)\s*\))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static UsharpSpaceDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var kind = string.Equals(d.Field(UsharpLanguage.FieldKind), UsharpLanguage.KindKeys,
            StringComparison.OrdinalIgnoreCase) ? UsharpSpaceKind.Keys : UsharpSpaceKind.Values;

        var axes = new List<UsharpAxis>();
        foreach (var raw in d.Continuation)
        {
            var m = AxisRegex.Match(raw.Trim());
            if (!m.Success) continue;
            var valueKind = kind == UsharpSpaceKind.Keys ? null
                : (m.Groups["kind"].Success ? m.Groups["kind"].Value.ToLowerInvariant() : "numeric");
            double? min = m.Groups["min"].Success && double.TryParse(m.Groups["min"].Value, out var mn) ? mn : null;
            double? max = m.Groups["max"].Success && double.TryParse(m.Groups["max"].Value, out var mx) ? mx : null;
            axes.Add(new UsharpAxis(m.Groups["name"].Value, valueKind, min, max));
        }
        return new UsharpSpaceDecl(d.Name, kind, axes, d.SourceFile, d.LineNumber);
    }
}
