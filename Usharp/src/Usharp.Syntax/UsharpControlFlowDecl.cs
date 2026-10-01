using Plate.DslMethod;
using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// 分支声明（R3，<c>[Branch]</c>）：<c>flow=</c> 所属流 + <c>node=</c> 承载节点 + <c>judge=</c> 判定键 +
/// <c>then=</c> / <c>else=</c> 目标。判定载体 = 结构化数据键（禁表达式文本）。
/// </summary>
public sealed record UsharpBranchDecl(
    string Name, string FlowId, string Node, string JudgeKey, string Then, string? Else,
    string SourceFile, int LineNumber)
{
    public static UsharpBranchDecl? From(DslDeclaration d)
    {
        if (d.Kind != DslSymbolKind.Branch || string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpBranchDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            d.Field(UsharpLanguage.FieldJudge).Trim(), d.Field(UsharpLanguage.FieldThen).Trim(),
            NullIfEmpty(d.Field(UsharpLanguage.FieldElse)), d.SourceFile, d.LineNumber);
    }

    private static string? NullIfEmpty(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// 多路分派声明（R3，<c>[Switch]</c>）：<c>flow=</c> + <c>node=</c> + <c>selector=</c> + <c>routes=&lt;v1=target1;…&gt;</c> + <c>default=</c>。
/// </summary>
public sealed record UsharpSwitchDecl(
    string Name, string FlowId, string Node, string SelectorKey,
    IReadOnlyDictionary<string, string> Routes, string? Default,
    string SourceFile, int LineNumber)
{
    public static UsharpSwitchDecl? From(DslDeclaration d)
    {
        if (d.Kind != DslSymbolKind.Switch || string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpSwitchDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            d.Field(UsharpLanguage.FieldSelector).Trim(),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldRoutes)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldDefault)), d.SourceFile, d.LineNumber);
    }

    private static string? NullIfEmpty(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>
/// 调用声明（R3，<c>[Call]</c>）：<c>flow=</c> + <c>node=</c> + <c>child=&lt;子流号&gt;</c> + 可选 <c>callee=</c> / <c>in=</c> / <c>out=</c>。
/// </summary>
public sealed record UsharpCallDecl(
    string Name, string FlowId, string Node, string? Callee, string ChildFlowId,
    IReadOnlyDictionary<string, string> InBindings, IReadOnlyDictionary<string, string> OutBindings,
    string SourceFile, int LineNumber)
{
    public static UsharpCallDecl? From(DslDeclaration d)
    {
        if (d.Kind != DslSymbolKind.Call || string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpCallDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            NullIfEmpty(d.Field(UsharpLanguage.FieldCallee)), d.Field(UsharpLanguage.FieldChildFlow).Trim(),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldInBindings)),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldOutBindings)),
            d.SourceFile, d.LineNumber);
    }

    private static string? NullIfEmpty(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>控制流声明的共享字段解析（R3）：<c>k=v;k2=v2</c> → 字典（非法段忽略）。</summary>
internal static class UsharpControlFlowFields
{
    public static IReadOnlyDictionary<string, string> ParsePairs(string field)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in UsharpLanguage.SplitSegments(field))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;
            var key = segment[..eq].Trim();
            var value = segment[(eq + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0) map[key] = value;
        }
        return map;
    }
}
