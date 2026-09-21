using Plate.DslMethod;
using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>流的一步：节点键 + 可选子链引用（<c>~ subflow</c> ⇒ 工作流连接链的唯一语法）。</summary>
public sealed record UsharpFlowStep(string Key, string? SubFlow)
{
    public static UsharpFlowStep? Parse(string line)
    {
        var text = line.Trim();
        if (text.Length == 0) return null;
        if (text.StartsWith(UsharpLanguage.StepMarker, StringComparison.Ordinal))
            text = text[UsharpLanguage.StepMarker.Length..].TrimStart(':', '=', ' ', '\t');

        string? sub = null;
        var tilde = text.IndexOf(UsharpLanguage.SubFlowMarker);
        if (tilde >= 0)
        {
            sub = text[(tilde + 1)..].Trim();
            text = text[..tilde].Trim();
            if (sub.Length == 0) sub = null;
        }
        return text.Length == 0 ? null : new UsharpFlowStep(text, sub);
    }
}

/// <summary>[Flow] Name: over=&lt;SpaceId&gt; 的语法视图（链 / 工作流 = 以链为节点的图）。</summary>
public sealed record UsharpFlowDecl(
    string FlowId,
    string? OverSpaceId,
    string? SuccessCriterion,
    IReadOnlyList<UsharpFlowStep> Steps,
    IReadOnlyList<DslReference> References,
    string SourceFile,
    int LineNumber)
{
    public static UsharpFlowDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;
        var steps = new List<UsharpFlowStep>();
        foreach (var raw in d.Continuation)
        {
            var step = UsharpFlowStep.Parse(raw);
            if (step is not null) steps.Add(step);
        }
        var over = d.Field(UsharpLanguage.FieldOver);
        var success = d.Field(UsharpLanguage.FieldSuccess);
        return new UsharpFlowDecl(d.Name,
            string.IsNullOrWhiteSpace(over) ? null : over.Trim(),
            string.IsNullOrWhiteSpace(success) ? null : success.Trim(),
            steps, d.References.ToList(), d.SourceFile, d.LineNumber);
    }
}

/// <summary>[Ref] Name: &lt;SpaceId&gt;:&lt;Key&gt; 的语法视图（带空间归属的引用 = 引用图的一条边）。</summary>
public sealed record UsharpRefDecl(
    string Name,
    string SpaceId,
    string Key,
    string? Source,
    string? Target,
    string? Intent,
    string? Edge,
    IReadOnlyList<DslReference> References,
    string SourceFile,
    int LineNumber)
{
    public static UsharpRefDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var space = d.Field(UsharpLanguage.FieldSpace);
        var key = d.Field(UsharpLanguage.FieldKey);

        // 简写形态：`[Ref] Name: <SpaceId>:<Key>`（body 内含冒号）
        if (string.IsNullOrWhiteSpace(space) || string.IsNullOrWhiteSpace(key))
        {
            var body = d.Body?.Trim() ?? "";
            var colon = body.IndexOf(':');
            if (colon > 0)
            {
                if (string.IsNullOrWhiteSpace(space)) space = body[..colon];
                if (string.IsNullOrWhiteSpace(key)) key = body[(colon + 1)..];
            }
        }

        return new UsharpRefDecl(
            d.Name, (space ?? "").Trim(), (key ?? "").Trim(),
            NullIfEmpty(d.Field(UsharpLanguage.FieldSrc)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldTgt)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldIntent)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldEdge)),
            d.References.ToList(), d.SourceFile, d.LineNumber);
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>把 DslDocument 投影为 Usharp 四类语法声明（按 <see cref="DslSymbolKind"/> 分派）。</summary>
public static class UsharpSyntaxProjector
{
    public static IReadOnlyList<UsharpSpaceDecl> Spaces(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Space)
            .Select(UsharpSpaceDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    public static IReadOnlyList<UsharpMatrixDecl> Matrices(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Matrix)
            .Select(UsharpMatrixDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    public static IReadOnlyList<UsharpFlowDecl> Flows(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Flow)
            .Select(UsharpFlowDecl.From).Where(s => s is not null).Select(s => s!).ToList();

    public static IReadOnlyList<UsharpRefDecl> Refs(DslDocument doc)
        => doc.Declarations.Where(d => d.Kind == DslSymbolKind.Ref)
            .Select(UsharpRefDecl.From).Where(s => s is not null).Select(s => s!).ToList();
}
