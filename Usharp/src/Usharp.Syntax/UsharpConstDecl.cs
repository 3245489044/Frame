using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>
/// <c>[Const] &lt;命名空间.类型&gt; [| kind=…] [| modifier=…] [| valueType=…]</c> 语法视图
/// （成员级声明面：把 C# 类型的编译期常量文件名↔值搬进声明，生成同名 <c>partial</c> 成员）。
/// <para>
/// 书写：头行给目标类型身份与种类修饰；续行 <c>MemberName = ValueExpr</c>（成员序 = 声明序）。
/// 续行中以 <c>#</c> 起首的行为注释（跳过，不进成员，也不进 <see cref="UnparsedLines"/>）；
/// 无法解析为 <c>Name = Expr</c> 的行原文落入 <see cref="UnparsedLines"/>（禁止静默丢弃）。
/// </para>
/// <para>
/// 本类只做<b>语法</b>投影：成员名文法 / 值表达式白名单 / 类型存在性由闸A 与生成器裁决。
/// </para>
/// </summary>
/// <param name="Target">目标类型全名（命名空间.类型；至少两段）。</param>
/// <param name="Kind">类型种类（<c>class|record|struct</c>；缺省 <c>class</c>）。</param>
/// <param name="Modifier">类型修饰（<c>static|sealed|abstract|plain</c>；缺省 <c>plain</c>）。</param>
/// <param name="ValueType">成员值类型（<c>string|int</c>；缺省 <c>string</c>）。</param>
/// <param name="Entries">成员条目（名 = 值原文；声明序）。</param>
/// <param name="UnparsedLines">无法解析的续行原文（禁静默丢弃）。</param>
/// <param name="SourceFile">来源文件（诊断归因）。</param>
/// <param name="LineNumber">声明行号（诊断归因）。</param>
public sealed record UsharpConstDecl(
    string Target,
    string Kind,
    string Modifier,
    string ValueType,
    IReadOnlyList<UsharpConfigEntry> Entries,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile,
    int LineNumber)
{
    /// <summary>类型种类缺省值。</summary>
    public const string DefaultKind = "class";

    /// <summary>修饰缺省值。</summary>
    public const string DefaultModifier = "plain";

    /// <summary>值类型缺省值。</summary>
    public const string DefaultValueType = "string";

    /// <summary>按成员名取值表达式原文（未命中 ⇒ null）。</summary>
    /// <param name="member">成员名（Ordinal 精确匹配）。</param>
    public string? Value(string member)
        => Entries.FirstOrDefault(e => string.Equals(e.Key, member, StringComparison.Ordinal))?.Value;

    /// <summary>投影单条 <c>[Const]</c>（无名 ⇒ null）。</summary>
    /// <param name="d">解析后的通用声明。</param>
    public static UsharpConstDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        // 头部 id 与字段的分隔符口径：`[Const] <target>: kind=… | …`（与既有单元一致）。
        // 兜底：若写法漏了冒号（target 与 `|` 连写），取首个 '|' 之前为 target（禁把字段塞进 target）。
        var target = d.Name;
        var bar = target.IndexOf('|');
        if (bar > 0) target = target[..bar].Trim();

        var kind = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.ConstFieldKind)) ?? DefaultKind;
        var modifier = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.ConstFieldModifier)) ?? DefaultModifier;
        var valueType = UsharpLanguage.NullIfEmpty(d.Field(UsharpLanguage.ConstFieldValueType)) ?? DefaultValueType;

        // 续行解析单源（与 [Config] 同一实现；禁逐类复制注释/切分逻辑）。
        var parsed = UsharpContinuation.Parse(d.Continuation);

        return new UsharpConstDecl(
            target, kind, modifier, valueType,
            parsed.Entries, parsed.UnparsedLines, d.SourceFile, d.LineNumber);
    }
}
