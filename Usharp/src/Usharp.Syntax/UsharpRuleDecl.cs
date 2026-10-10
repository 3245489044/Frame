using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Rule] Name</c> 语法视图（规则面：条件 → 动作）。
/// <para>书写：<c>[Rule] &lt;name&gt;: scope=&lt;s&gt; | action=&lt;a&gt; | condition=&lt;c&gt; | reason=&lt;r&gt; | enabled=&lt;bool&gt;</c>；
/// 续行 = 条件行（逐行原文，序 = 声明序）。</para>
/// <para>本类只做**语法**投影：条件表达式求值语义由消费侧（既有规则引擎）承担，本类不新增求值机制。</para>
/// </summary>
public sealed record UsharpRuleDecl(
    string Name,
    string? Scope,
    string? Action,
    string? Condition,
    string? Reason,
    bool? Enabled,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>投影单条 <c>[Rule]</c>（无名 ⇒ null）。</summary>
    public static UsharpRuleDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var enabled = d.HasField(UsharpLanguage.FieldEnabled)
            && bool.TryParse(d.Field(UsharpLanguage.FieldEnabled), out var parsed)
            ? parsed
            : (bool?)null;

        var conditions = new List<string>();
        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            conditions.Add(line);
        }

        return new UsharpRuleDecl(
            d.Name,
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldScope)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldAction)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldCondition)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldReason)),
            enabled,
            conditions,
            unparsed,
            d.SourceFile,
            d.LineNumber);
    }
}
