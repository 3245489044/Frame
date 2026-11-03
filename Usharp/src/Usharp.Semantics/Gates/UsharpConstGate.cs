using System.Text.RegularExpressions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Validation;
using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 闸A：<c>[Const]</c> 成员级声明结构闭包（C1–C8）。
/// <para>
/// 判据载体 = 声明面结构事实（target 形态 / 受控词 / 成员名 / 表达式白名单 / 计数），
/// <b>不含</b>「仓库内该类型是否存在」（类型存在性属领域坐标，归生成器；内核零领域语义）。
/// </para>
/// </summary>
internal sealed class ConstGateRule : IGateRule<UsharpGateModel>
{
    private static readonly Regex TargetShape =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant);

    private static readonly Regex MemberName =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private static readonly Regex StringLiteral =
        new("^\"([^\"\\\\]|\\\\.)*\"$", RegexOptions.CultureInvariant);

    private static readonly Regex VerbatimStringLiteral =
        new("^@\"([^\"]|\"\")*\"$", RegexOptions.CultureInvariant);

    private static readonly Regex IntegerLiteral =
        new(@"^-?(0|[1-9]\d*)$", RegexOptions.CultureInvariant);

    private static readonly Regex NameOfReference =
        new(@"^nameof\(\s*[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){1,}\s*\)$", RegexOptions.CultureInvariant);
    private static readonly Regex MemberReference =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$", RegexOptions.CultureInvariant);

    /// <summary>C# 关键字（成员名禁止）。</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    public string Name => "usharp.const";

    public void Check(UsharpGateModel model, IConversionDiagnosticSink sink)
    {
        // 跨单元：同 target 的成员名表（valueType 拆分后仍须唯一）与类型身份一致性。
        var membersByTarget = new Dictionary<string, Dictionary<string, (string File, int Line)>>(StringComparer.Ordinal);
        var identityByTarget = new Dictionary<string, (string Kind, string Modifier, string File, int Line)>(StringComparer.Ordinal);

        foreach (var unit in model.Consts)
        {
            // C1 target 形态
            if (!TargetShape.IsMatch(unit.Target ?? string.Empty))
            {
                Report(sink, UsharpCodes.ConstTargetInvalid,
                    $"[Const] {unit.Target}: target 须为「命名空间.类型」（至少两段，禁只有类型名）",
                    unit);
                continue;
            }

            // C2 kind 白名单
            if (Array.IndexOf(UsharpLanguage.ConstKinds, unit.Kind) < 0)
            {
                Report(sink, UsharpCodes.ConstKindInvalid,
                    $"[Const] {unit.Target}: kind='{unit.Kind}' 不在受控词域（{string.Join('/', UsharpLanguage.ConstKinds)}；interface 不允许 const）",
                    unit);
                continue;
            }

            // C3 modifier 与 kind 组合
            if (!IsModifierAllowed(unit.Kind, unit.Modifier))
            {
                Report(sink, UsharpCodes.ConstModifierInvalid,
                    $"[Const] {unit.Target}: modifier='{unit.Modifier}' 与 kind='{unit.Kind}' 不兼容"
                    + (string.Equals(unit.Kind, "record", StringComparison.Ordinal)
                        ? "（record 仅允许 sealed/plain；static record 不存在）"
                        : "（class/struct 允许 static/sealed/abstract/plain）"),
                    unit);
            }

            // 值类型受控词（类型身份的一部分）
            if (Array.IndexOf(UsharpLanguage.ConstValueTypes, unit.ValueType) < 0)
            {
                Report(sink, UsharpCodes.ConstKindInvalid,
                    $"[Const] {unit.Target}: valueType='{unit.ValueType}' 不在受控词域（{string.Join('/', UsharpLanguage.ConstValueTypes)}）",
                    unit);
            }

            CheckIdentityAndMembers(sink, unit, identityByTarget, membersByTarget);
        }
    }

    /// <summary>同 target 的身份一致性 + 成员名/重名/表达式/未解析行判据（C4–C8）。</summary>
    private static void CheckIdentityAndMembers(
        IConversionDiagnosticSink sink,
        UsharpConstDecl unit,
        Dictionary<string, (string Kind, string Modifier, string File, int Line)> identityByTarget,
        Dictionary<string, Dictionary<string, (string File, int Line)>> membersByTarget)
    {
        if (identityByTarget.TryGetValue(unit.Target, out var identity))
        {
            if (!string.Equals(identity.Kind, unit.Kind, StringComparison.Ordinal)
                || !string.Equals(identity.Modifier, unit.Modifier, StringComparison.Ordinal))
            {
                Report(sink, UsharpCodes.ConstTargetConflict,
                    $"[Const] {unit.Target}: 同 target 多个单元的 kind/modifier 不一致"
                    + $"（首次 {identity.Kind}/{identity.Modifier} @{identity.File}:{identity.Line}，本次 {unit.Kind}/{unit.Modifier}）",
                    unit);
            }
        }
        else
        {
            identityByTarget[unit.Target] = (unit.Kind, unit.Modifier, unit.SourceFile, unit.LineNumber);
        }

        if (!membersByTarget.TryGetValue(unit.Target, out var seen))
            membersByTarget[unit.Target] = seen = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);

        var localSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in unit.Entries)
        {
            // C4 成员名文法
            if (!MemberName.IsMatch(entry.Key) || Keywords.Contains(entry.Key))
            {
                Report(sink, UsharpCodes.ConstMemberInvalid,
                    $"[Const] {unit.Target}: 成员名 '{entry.Key}' 非法（须匹配 ^[A-Za-z_]\\w*$ 且非 C# 关键字）",
                    unit);
                continue;
            }

            // C5 单元内重名
            if (!localSeen.Add(entry.Key))
            {
                Report(sink, UsharpCodes.ConstMemberDuplicate,
                    $"[Const] {unit.Target}: 成员 '{entry.Key}' 在本单元重复声明",
                    unit);
            }

            // C6 跨单元同 target 重名
            if (seen.TryGetValue(entry.Key, out var origin))
            {
                Report(sink, UsharpCodes.ConstMemberDuplicate,
                    $"[Const] {unit.Target}: 成员 '{entry.Key}' 与另一单元重复（首次 @{origin.File}:{origin.Line}）",
                    unit);
            }
            else
            {
                seen[entry.Key] = (unit.SourceFile, unit.LineNumber);
            }

            // C7 值表达式白名单
            if (!IsAllowedExpression(entry.Value))
            {
                Report(sink, UsharpCodes.ConstValueExpressionInvalid,
                    $"[Const] {unit.Target}.{entry.Key}: 值表达式 '{entry.Value}' 不在白名单"
                    + "（仅允许字符串字面量 / 整数字面量 / nameof(<Target>) / <Target>.<Member>；禁运算符/方法调用）",
                    unit);
            }
        }

        // C8 未解析续行
        foreach (var line in unit.UnparsedLines)
        {
            Report(sink, UsharpCodes.ConstLineUnparsed,
                $"[Const] {unit.Target}: 无法解析的续行 '{line}'（须为 Name = Expr）",
                unit);
        }
    }

    private static bool IsModifierAllowed(string kind, string modifier)
    {
        if (Array.IndexOf(UsharpLanguage.ConstModifiers, modifier) < 0) return false;

        return string.Equals(kind, "record", StringComparison.Ordinal)
            ? modifier is "sealed" or "plain"
            : modifier is "static" or "sealed" or "abstract" or "plain";
    }

    /// <summary>值表达式白名单（C7）：字面量 / <c>nameof(&lt;Target&gt;)</c> / <c>&lt;Target&gt;.&lt;Member&gt;</c>。</summary>
    /// <param name="expression">续行值原文。</param>
    internal static bool IsAllowedExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;

        var text = expression.Trim();
        return StringLiteral.IsMatch(text)
            || VerbatimStringLiteral.IsMatch(text)
            || IntegerLiteral.IsMatch(text)
            || NameOfReference.IsMatch(text)
            || MemberReference.IsMatch(text);
    }

    private static void Report(IConversionDiagnosticSink sink, string code, string message, UsharpConstDecl unit)
        => sink.Report(UsharpGateDiagnostics.Error(code, message, unit.SourceFile, unit.LineNumber));
}
