using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Relation] &lt;operationId&gt;: &lt;显示名&gt; | edge=… | src=… | tgt=… | flow=… | namespace=…</c> 语法视图。
/// <para>只装声明事实：边类型/端点/编排流/作用域轴的合法性由闸A（<c>UsharpRelationGate</c>）裁决；
/// 运行期由 <c>RelationPlanCatalog</c> 解析，禁调用方传裸 <c>refType</c>。</para>
/// </summary>
public sealed record UsharpRelationDecl(
    string OperationId,
    string? Display,
    string Edge,
    string Source,
    string Target,
    string Flow,
    string Namespace,
    IReadOnlyList<string> Exists,
    string Duplicate,
    string OnMissing,
    string Arity,
    string Intent,
    string? Emit,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber,
    string Container = "",
    string OnMismatch = "",
    string Provision = "")
{
    /// <summary>投影单条 <c>[Relation]</c>（无名 ⇒ null；未知续行进 <see cref="UnparsedLines"/>）。</summary>
    public static UsharpRelationDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        return new UsharpRelationDecl(
            d.Name.Trim(),
            UsharpLineReader.NullIfEmpty(d.Body),
            (d.Field(UsharpLanguage.FieldEdge) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldSrc) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldTgt) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldFlow) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldNamespace) ?? string.Empty).Trim(),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FieldExists)),
            (d.Field(UsharpLanguage.FieldDuplicate) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldOnMissing) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldArity) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldIntent) ?? string.Empty).Trim(),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.FieldEmit)),
            Unparsed(d),
            d.SourceFile,
            d.LineNumber,
            (d.Field("container") ?? string.Empty).Trim(),
            (d.Field("on_mismatch") ?? string.Empty).Trim(),
            (d.Field("provision") ?? string.Empty).Trim());
    }

    /// <summary>续行去空白后保留（空 ⇒ 空表）；由闸A 统一报 <c>US8115</c> 类错误。</summary>
    internal static IReadOnlyList<string> Unparsed(DslDeclaration d)
        => d.Continuation
            .Select(line => (line ?? string.Empty).Trim())
            .Where(line => line.Length > 0)
            .ToList();
}

/// <summary>
/// <c>[Invariant] &lt;id&gt;: &lt;显示名&gt; | kind=… | edge=… | on_violation=… | scope=…</c> 语法视图。
/// </summary>
public sealed record UsharpInvariantDecl(
    string InvariantId,
    string? Display,
    string Kind,
    string Edge,
    string OnViolation,
    string? Scope,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber,
    IReadOnlyList<string>? Pre = null,
    IReadOnlyList<string>? Post = null,
    string Posture = "")
{
    /// <summary>投影单条 <c>[Invariant]</c>（无名 ⇒ null）。</summary>
    public static UsharpInvariantDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        return new UsharpInvariantDecl(
            d.Name.Trim(),
            UsharpLineReader.NullIfEmpty(d.Body),
            (d.Field(UsharpLanguage.FieldKind) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldEdge) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldOnViolation) ?? string.Empty).Trim(),
            UsharpLineReader.NullIfEmpty(d.Field(UsharpLanguage.FieldScope)),
            UsharpRelationDecl.Unparsed(d),
            d.SourceFile,
            d.LineNumber,
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FieldGuardPre), ',', ';'),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FieldGuardPost), ',', ';'),
            (d.Field(UsharpLanguage.FieldPosture) ?? string.Empty).Trim());
    }
}

/// <summary>
/// <c>[Derive] &lt;id&gt;: &lt;显示名&gt; | from=… | to=… | via=e1,e2 | op=chain | normalize=… | order=… | empty=…</c> 语法视图。
/// </summary>
public sealed record UsharpDeriveDecl(
    string DeriveId,
    string? Display,
    string From,
    string To,
    IReadOnlyList<string> Via,
    string Op,
    string Normalize,
    string Order,
    IReadOnlyList<string> Empty,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Derive]</c>（无名 ⇒ null；<c>via</c> 保声明序 = 路径序）。</summary>
    /// <remarks>方法名用 <c>FromDeclaration</c>：记录已有 <c>From</c> 属性（派生起点），二者不得同名。</remarks>
    public static UsharpDeriveDecl? FromDeclaration(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        return new UsharpDeriveDecl(
            d.Name.Trim(),
            UsharpLineReader.NullIfEmpty(d.Body),
            (d.Field(UsharpLanguage.FieldFrom) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldTo) ?? string.Empty).Trim(),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FieldVia)),
            (d.Field(UsharpLanguage.FieldOp) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldNormalize) ?? string.Empty).Trim(),
            (d.Field(UsharpLanguage.FieldOrder) ?? string.Empty).Trim(),
            UsharpLineReader.SplitList(d.Field(UsharpLanguage.FieldEmpty)),
            UsharpRelationDecl.Unparsed(d),
            d.SourceFile,
            d.LineNumber);
    }
}
