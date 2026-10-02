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
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldElse)), d.SourceFile, d.LineNumber);
    }
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
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldDefault)), d.SourceFile, d.LineNumber);
    }
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
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldCallee)), d.Field(UsharpLanguage.FieldChildFlow).Trim(),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldInBindings)),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldOutBindings)),
            d.SourceFile, d.LineNumber);
    }
}

/// <summary>
/// 区域声明（R3+，<c>[Region]</c>）：<c>flow=</c> 所属流 + <c>node=</c> 父层承载节点 +
/// <c>child=&lt;子流号&gt;</c> + <c>iter=once|while_true|until_true</c> + 可选 <c>judge=</c>（判定端口）/
/// <c>max=</c>（迭代上限）+ 可选 <c>in=</c> / <c>out=</c>（区域出入映射）。
/// </summary>
public sealed record UsharpRegionDecl(
    string Name, string FlowId, string ParentNode, string ChildFlowId,
    string Iteration, string? JudgePort, int? MaxIterations,
    IReadOnlyDictionary<string, string> InBindings, IReadOnlyDictionary<string, string> OutBindings,
    string SourceFile, int LineNumber)
{
    public static UsharpRegionDecl? From(DslDeclaration d)
    {
        if (d.Kind != DslSymbolKind.Region || string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpRegionDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            d.Field(UsharpLanguage.FieldChildFlow).Trim(),
            d.Field(UsharpLanguage.FieldIteration).Trim(),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldJudge)),
            ParseInt(d.Field(UsharpLanguage.FieldMaxIterations)),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldInBindings)),
            UsharpControlFlowFields.ParsePairs(d.Field(UsharpLanguage.FieldOutBindings)),
            d.SourceFile, d.LineNumber);
    }

    private static int? ParseInt(string v) => int.TryParse(v, out var n) ? n : null;
}

/// <summary>
/// 受控边声明（R3+，<c>[When]</c>）：<c>flow=</c> 所属流 + <c>from=</c> 前驱 + <c>to=</c> 后继 +
/// <c>judge=</c> 判定键 —— judge 判真后该边才激活（G3 受控边门控）。
/// </summary>
public sealed record UsharpWhenDecl(
    string Name, string FlowId, string FromNode, string To, string JudgeKey,
    string SourceFile, int LineNumber)
{
    public static UsharpWhenDecl? From(DslDeclaration d)
    {
        if (d.Kind != DslSymbolKind.When || string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpWhenDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldFrom).Trim(),
            d.Field(UsharpLanguage.FieldTo).Trim(), d.Field(UsharpLanguage.FieldJudge).Trim(),
            d.SourceFile, d.LineNumber);
    }
}

/// <summary>
/// 受护作用域声明（R3+，<c>[Guard]</c>）：<c>flow=</c> 所属流 + <c>nodes=&lt;n1;n2;…&gt;</c> ——
/// 作用域内节点失败 → 吞并继续（G4 容错）。
/// </summary>
public sealed record UsharpGuardDecl(
    string Name, string FlowId, IReadOnlyList<string> NodeIds,
    string SourceFile, int LineNumber)
{
    public static UsharpGuardDecl? From(DslDeclaration d)
    {
        if (d.Kind != DslSymbolKind.Guard || string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpGuardDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(),
            UsharpLanguage.SplitSegments(d.Field(UsharpLanguage.FieldNodes)).ToList(),
            d.SourceFile, d.LineNumber);
    }
}

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
