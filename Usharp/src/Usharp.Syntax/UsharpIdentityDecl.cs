using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Entity] answer: 终答 | scope=turn | container=conversation | cardinality=one-per-turn | ...</c>
/// 语义轴声明：实体「是什么」（与语法轴 family/surface「怎么写」正交）。
/// </summary>
public sealed record UsharpEntityDecl(
    string Id,
    string Scope,
    string Container,
    string Cardinality,
    bool Terminal,
    string Role,
    string Authority,
    string Family,
    string MemberType,
    string Description,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Entity]</c>（无名 ⇒ null）。</summary>
    public static UsharpEntityDecl? From(DslDeclaration d)
    {
        var parsed = UsharpIdentitySyntax.Parse(d);
        if (parsed.Id.Length == 0) return null;
        return new UsharpEntityDecl(
            parsed.Id,
            parsed.Get("scope"),
            parsed.Get("container"),
            parsed.Get("cardinality"),
            string.Equals(parsed.Get("terminal"), "true", StringComparison.OrdinalIgnoreCase),
            parsed.Get("role"),
            parsed.Get("authority"),
            parsed.Get("family"),
            parsed.Get("member"),
            parsed.Body,
            d.SourceFile,
            d.LineNumber);
    }
}

/// <summary><c>[KeyLexicon] ProcessKind: stage, endpoint, ...</c> 受控词表声明（词表名 + 值域）。</summary>
public sealed record UsharpKeyLexiconDecl(
    string Name,
    string Description,
    IReadOnlyList<string> Values,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[KeyLexicon]</c>（无名 ⇒ null；值域 = body ∪ values=）。</summary>
    public static UsharpKeyLexiconDecl? From(DslDeclaration d)
    {
        var parsed = UsharpIdentitySyntax.Parse(d);
        if (parsed.Id.Length == 0) return null;

        var raw = parsed.Get("values");
        if (raw.Length == 0) raw = parsed.Body;
        var values = raw
            .Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new UsharpKeyLexiconDecl(parsed.Id, string.Empty, values, d.SourceFile, d.LineNumber);
    }
}

/// <summary>
/// <c>[AnswerShape] answer/1 | container=required | ... | default=Block</c> / <c>[WindowShape]</c>
/// 形状契约（必填字段 + 终结规则 + 未命中处置）；<c>Kind</c> ∈ <c>Answer</c>|<c>Window</c>。
/// </summary>
public sealed record UsharpKeyShapeDecl(
    string Kind,
    string Id,
    IReadOnlyDictionary<string, string> Constraints,
    string Default,
    string Crit,
    string SourceFile,
    int LineNumber)
{
    /// <summary>取约束值（未声明 ⇒ null，不猜默认）。</summary>
    public string? Get(string name)
        => Constraints.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>是否声明某约束。</summary>
    public bool Has(string name) => Get(name) is not null;

    /// <summary>投影单条形状声明（无名 ⇒ null）。</summary>
    public static UsharpKeyShapeDecl? From(DslDeclaration d, string kind)
    {
        var parsed = UsharpIdentitySyntax.Parse(d);
        if (parsed.Id.Length == 0) return null;

        return new UsharpKeyShapeDecl(
            kind, parsed.Id, parsed.Fields, parsed.Get("default"), parsed.Get("crit"),
            d.SourceFile, d.LineNumber);
    }
}

/// <summary><c>[KeyPolicy] AnswerSingle | entity=answer | cardinality=one-per-turn | writer=... | ...</c> 写侧策略。</summary>
public sealed record UsharpIdentityPolicyDecl(
    string Id,
    string Entity,
    string Cardinality,
    bool Terminal,
    string Writer,
    string OnUnresolvable,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[KeyPolicy]</c>（无名 ⇒ null）。</summary>
    public static UsharpIdentityPolicyDecl? From(DslDeclaration d)
    {
        var parsed = UsharpIdentitySyntax.Parse(d);
        if (parsed.Id.Length == 0) return null;
        return new UsharpIdentityPolicyDecl(
            parsed.Id,
            parsed.Get("entity"),
            parsed.Get("cardinality"),
            string.Equals(parsed.Get("terminal"), "true", StringComparison.OrdinalIgnoreCase),
            parsed.Get("writer"),
            parsed.Get("on_unresolvable"),
            d.SourceFile,
            d.LineNumber);
    }
}

/// <summary><c>[KeyBridge] AnswerToCanon | from=domain.answer | to=canonical | reversible=true</c> 跨域映射（不含语法形）。</summary>
public sealed record UsharpKeyBridgeDecl(
    string Id,
    string From,
    string To,
    string Rule,
    bool Reversible,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[KeyBridge]</c>（无名 ⇒ null）。</summary>
    public static UsharpKeyBridgeDecl? Project(DslDeclaration d)
    {
        var parsed = UsharpIdentitySyntax.Parse(d);
        if (parsed.Id.Length == 0) return null;
        return new UsharpKeyBridgeDecl(
            parsed.Id,
            parsed.Get("from"),
            parsed.Get("to"),
            parsed.Get("rule"),
            string.Equals(parsed.Get("reversible"), "true", StringComparison.OrdinalIgnoreCase),
            d.SourceFile,
            d.LineNumber);
    }
}

/// <summary>
/// 语义声明行的极简解析（<c>[Prefix] id: body | k=v | ...</c>）：名称取首个 <c>|</c> 之前的 head，
/// 字段取其后逐段 <c>k=v</c>（与 <c>[KeySpace]</c> 续行同形；不依赖 <c>DslDeclaration.Fields</c>）。
/// </summary>
internal readonly struct UsharpIdentitySyntax
{
    private UsharpIdentitySyntax(string id, string body, IReadOnlyDictionary<string, string> fields)
    {
        Id = id;
        Body = body;
        Fields = fields;
    }

    public string Id { get; }
    public string Body { get; }
    public IReadOnlyDictionary<string, string> Fields { get; }

    public string Get(string key)
        => Fields.TryGetValue(key, out var value) ? value : string.Empty;

    public static UsharpIdentitySyntax Parse(DslDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var raw = (declaration.RawLine ?? string.Empty).Trim();
        var close = raw.IndexOf(']');
        var rest = close >= 0 ? raw[(close + 1)..].Trim() : raw;

        var segments = rest.Split('|');
        var head = segments[0].Trim();
        var colon = head.IndexOf(':');
        var id = (colon >= 0 ? head[..colon] : head).Trim();
        var body = (colon >= 0 ? head[(colon + 1)..] : string.Empty).Trim();

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i].Trim();
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;
            fields[segment[..eq].Trim()] = segment[(eq + 1)..].Trim();
        }

        return new UsharpIdentitySyntax(id, body, fields);
    }
}
