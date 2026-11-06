using Plate.DslMethod;
using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// 控制流绑定段（<c>in=</c>/<c>out=</c>/<c>routes=</c>）的一处词法问题：
/// 坏段（无 <c>=</c> / 空键 / 空值）或重复键 —— 由闸A 报码，禁止静默丢弃。
/// </summary>
/// <param name="Field">字段名（<c>in</c>/<c>out</c>/<c>routes</c>）。</param>
/// <param name="Segment">问题段原文（重复键时为键名）。</param>
/// <param name="IsDuplicate">true = 重复键；false = 无法解析段。</param>
public sealed record UsharpControlFlowBindingIssue(string Field, string Segment, bool IsDuplicate);

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
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
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
    /// <summary>绑定段词法问题（坏段 / 重复键；空表 = 全部解析成功）。</summary>
    public IReadOnlyList<UsharpControlFlowBindingIssue> BindingIssues { get; init; }
        = Array.Empty<UsharpControlFlowBindingIssue>();

    public static UsharpSwitchDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var routes = UsharpControlFlowFields.Parse(d.Field(UsharpLanguage.FieldRoutes), UsharpLanguage.FieldRoutes);
        return new UsharpSwitchDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            d.Field(UsharpLanguage.FieldSelector).Trim(),
            routes.Map,
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldDefault)), d.SourceFile, d.LineNumber)
        {
            BindingIssues = routes.Issues,
        };
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
    /// <summary>绑定段词法问题（<c>in=</c> ∪ <c>out=</c>；空表 = 全部解析成功）。</summary>
    public IReadOnlyList<UsharpControlFlowBindingIssue> BindingIssues { get; init; }
        = Array.Empty<UsharpControlFlowBindingIssue>();

    public static UsharpCallDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var inBindings = UsharpControlFlowFields.Parse(d.Field(UsharpLanguage.FieldInBindings), UsharpLanguage.FieldInBindings);
        var outBindings = UsharpControlFlowFields.Parse(d.Field(UsharpLanguage.FieldOutBindings), UsharpLanguage.FieldOutBindings);
        return new UsharpCallDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldCallee)), d.Field(UsharpLanguage.FieldChildFlow).Trim(),
            inBindings.Map, outBindings.Map,
            d.SourceFile, d.LineNumber)
        {
            BindingIssues = inBindings.Issues.Concat(outBindings.Issues).ToList(),
        };
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
    /// <summary>绑定段词法问题（<c>in=</c> ∪ <c>out=</c>；空表 = 全部解析成功）。</summary>
    public IReadOnlyList<UsharpControlFlowBindingIssue> BindingIssues { get; init; }
        = Array.Empty<UsharpControlFlowBindingIssue>();

    public static UsharpRegionDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var inBindings = UsharpControlFlowFields.Parse(d.Field(UsharpLanguage.FieldInBindings), UsharpLanguage.FieldInBindings);
        var outBindings = UsharpControlFlowFields.Parse(d.Field(UsharpLanguage.FieldOutBindings), UsharpLanguage.FieldOutBindings);
        return new UsharpRegionDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(), d.Field(UsharpLanguage.FieldNode).Trim(),
            d.Field(UsharpLanguage.FieldChildFlow).Trim(),
            d.Field(UsharpLanguage.FieldIteration).Trim(),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldJudge)),
            ParseInt(d.Field(UsharpLanguage.FieldMaxIterations)),
            inBindings.Map, outBindings.Map,
            d.SourceFile, d.LineNumber)
        {
            BindingIssues = inBindings.Issues.Concat(outBindings.Issues).ToList(),
        };
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
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
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
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        return new UsharpGuardDecl(
            d.Name, d.Field(UsharpLanguage.FieldFlow).Trim(),
            UsharpLanguage.SplitSegments(d.Field(UsharpLanguage.FieldNodes)).ToList(),
            d.SourceFile, d.LineNumber);
    }
}

internal static class UsharpControlFlowFields
{
    /// <summary>
    /// 解析 <c>k=v;…</c> 字段：**保留坏段与重复键**（词法内核 = <see cref="StrictKeyValueArguments"/>）。
    /// </summary>
    /// <param name="field">字段原文（<c>in=</c>/<c>out=</c>/<c>routes=</c>）。</param>
    /// <param name="fieldName">字段名（问题归因用）。</param>
    public static (IReadOnlyDictionary<string, string> Map, IReadOnlyList<UsharpControlFlowBindingIssue> Issues)
        Parse(string? field, string fieldName)
    {
        var bag = StrictKeyValueArguments.Parse(
            UsharpLanguage.SplitSegments(field), skip: 0, comparer: StringComparer.Ordinal);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var issues = new List<UsharpControlFlowBindingIssue>(bag.Unparsed.Count + bag.DuplicateKeys.Count);

        foreach (var segment in bag.Unparsed)
            issues.Add(new UsharpControlFlowBindingIssue(fieldName, segment, IsDuplicate: false));

        foreach (var pair in bag.Fields)
        {
            // 空值沿用既有口径「丢弃」，但不再静默：记入坏段。
            if (pair.Value.Length == 0)
            {
                issues.Add(new UsharpControlFlowBindingIssue(fieldName, $"{pair.Key}=", IsDuplicate: false));
                continue;
            }
            map[pair.Key] = pair.Value;
        }

        foreach (var key in bag.DuplicateKeys)
            issues.Add(new UsharpControlFlowBindingIssue(fieldName, key, IsDuplicate: true));

        return (map, issues);
    }

    /// <summary>仅取字典（不关心问题的消费方；问题可见性由闸A 经 <c>BindingIssues</c> 负责）。</summary>
    public static IReadOnlyDictionary<string, string> ParsePairs(string? field)
        => Parse(field, string.Empty).Map;
}
