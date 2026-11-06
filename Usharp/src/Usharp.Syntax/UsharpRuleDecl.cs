using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// 规则体的一条路径项（<c>&lt;path&gt; = &lt;value&gt;</c>）：<c>path</c> 以 <c>.</c> 表嵌套、<c>[n]</c> 表数组下标，
/// 承载 <c>when</c>/<c>then</c>/<c>require</c> 多行子树（框架续行已去缩进 ⇒ 以路径编码树形，禁缩进推断）。
/// </summary>
/// <param name="Path">路径（如 <c>when.all.match.strategy</c> / <c>require.children[0].kind</c>）。</param>
/// <param name="Value">值原文（已去空白；含 <c>=</c> 时仅首个作为分隔）。</param>
public sealed record UsharpRuleEntry(string Path, string Value);

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
    string? NextPromptHint,
    bool? Enabled,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<UsharpRuleEntry> Body,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>取某子块（<c>when</c>/<c>then</c>/<c>require</c>）的路径项（path 以 <c>&lt;root&gt;.</c> 开头）。</summary>
    /// <param name="root">子块根词（如 <c>when</c>）。</param>
    public IReadOnlyList<UsharpRuleEntry> Block(string root)
        => Body.Where(e => e.Path.StartsWith(root + ".", StringComparison.Ordinal)).ToList();

    /// <summary><c>enabled=</c> 原文（null = 未声明；非法布尔由闸A 报 US0010，禁止静默变 null）。</summary>
    public string? EnabledRaw { get; init; }

    /// <summary>按完整路径取首个值（未命中 ⇒ null）。</summary>
    /// <param name="path">路径项（如 <c>when.all.match.strategy</c>）。</param>
    public string? Value(string path)
        => Body.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.Ordinal))?.Value;

    /// <summary>投影单条 <c>[Rule]</c>（无名 ⇒ null）。</summary>
    public static UsharpRuleDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var enabledRaw = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldEnabled));
        var enabled = enabledRaw is not null && bool.TryParse(enabledRaw, out var parsed) ? parsed : (bool?)null;

        var conditions = new List<string>();
        var body = new List<UsharpRuleEntry>();
        var unparsed = new List<string>();
        foreach (var raw in d.Continuation)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // 无 `=` ⇒ 自由条件行（简单形态，如规则集 action 行）；有 `=` ⇒ 路径项（when/then/require 子树）。
            var bag = StrictKeyValueArguments.Parse(new[] { line }, skip: 0, comparer: StringComparer.Ordinal);
            if (bag.Fields.Count == 0)
            {
                // 无 `=` ⇒ 条件行；空键（`=x`）⇒ 坏段（与旧口径一致：不静默当条件）。
                if (line.StartsWith('=')) { unparsed.Add(line); continue; }
                conditions.Add(line);
                continue;
            }

            var pair = bag.Fields.First();
            var path = pair.Key;
            var value = pair.Value;
            if (path.Length == 0) { unparsed.Add(line); continue; }

            body.Add(new UsharpRuleEntry(path, value));
        }

        return new UsharpRuleDecl(
            d.Name,
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldScope)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldAction)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldCondition)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldReason)),
            UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.FieldNextPromptHint)),
            enabled,
            conditions,
            body,
            unparsed,
            d.SourceFile,
            d.LineNumber)
        {
            EnabledRaw = enabledRaw,
        };
    }
}
