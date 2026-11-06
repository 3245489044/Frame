using System.Globalization;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;
using Usharp.Syntax;

namespace Usharp.Compute;

public enum ComputeUnitKind
{
    Expr,
    Fn,
    Proc
}

public enum ComputePurityPolicy
{
    Strict,
    Scoped,
    Impure
}

public enum ComputeEffect
{
    KeyRead,
    KeyWrite,
    Emit,
    Clock,
    Random
}

public sealed record ComputeArgument(string Name, ComputeType Type);

public sealed record ComputeUnitHeader(
    string CalcId,
    ComputeUnitKind Kind,
    IReadOnlyList<ComputeArgument> Arguments,
    ComputeType ReturnType,
    bool HasReturn,
    ComputePurityPolicy Purity,
    IReadOnlyList<ComputeEffect> Effects,
    long? Budget,
    long Slice,
    long Heap,
    long Depth,
    decimal? ShareMax,
    string SourceFile,
    int LineNumber)
{
    public const long DefaultSlice = 1000;
    public const long DefaultHeap = 1000;
    public const long DefaultDepth = 64;
}

/// <summary>
/// 声明头解析。头只接受 k=v 字段白名单；任何值一旦出现计算标记字符（+ - * / ( ) = ;）即 US8001，
/// 且每个键按自身语法逐项校验。正文语句只存在于缩进续行通道（d.Continuation）。
/// </summary>
public static class ComputeHeaderParser
{
    public const string FieldKind = "kind";
    public const string FieldArgs = "args";
    public const string FieldReturns = "returns";
    public const string FieldPure = "pure";
    public const string FieldEffects = "effects";
    public const string FieldBudget = "budget";
    public const string FieldSlice = "slice";
    public const string FieldHeap = "heap";
    public const string FieldDepth = "depth";
    public const string FieldShareMax = "share_max";

    public static readonly IReadOnlyList<string> AllowedFields = new[]
    {
        FieldKind, FieldArgs, FieldReturns, FieldPure, FieldEffects,
        FieldBudget, FieldSlice, FieldHeap, FieldDepth, FieldShareMax
    };

    /// <summary>声明头值的禁字符：这些字符只可能出现在表达式/语句里。</summary>
    public static readonly IReadOnlyList<char> ForbiddenValueChars = new[]
    {
        '+', '-', '*', '/', '(', ')', '=',
    };

    private static readonly Regex NotIdentifier = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    public static bool TryParse(DslDeclaration declaration, out ComputeUnitHeader? header,
        out IReadOnlyList<ComputeDiagnostic> diagnostics)
    {
        var errors = new List<ComputeDiagnostic>();
        header = null;
        var file = declaration.SourceFile;
        var line = declaration.LineNumber;

        if (!UsharpIdentifier.Matches(declaration.Name))
        {
            errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                $"计算单元名 '{declaration.Name}' 不是合法标识符（[A-Za-z_][A-Za-z0-9_]*）", file, line));
        }

        var tail = HeaderTail(declaration);
        var parts = FieldParser.SplitOnPipe(tail);
        var fields = FieldParser.Parse(parts, out var prose);
        if (prose.Length > 0)
        {
            errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                $"计算声明头只接受 k=v 字段，出现非字段段 '{prose}'", file, line));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var key = field.Key;
            var value = field.Value;
            if (key.Length == 0)
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid, "空字段名", file, line));
                continue;
            }

            if (!AllowedFields.Contains(key, StringComparer.Ordinal))
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"未知声明头字段 '{key}'（白名单：{string.Join('/', AllowedFields)}）", file, line));
                continue;
            }

            if (values.ContainsKey(key))
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"声明头字段 '{key}' 重复", file, line));
                continue;
            }

            if (value.Length == 0)
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"声明头字段 '{key}' 的值为空", file, line));
                continue;
            }

            var bad = value.FirstOrDefault(c => ForbiddenValueChars.Contains(c));
            if (bad != '\0')
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"声明头字段 '{key}' 的取值含计算标记字符 '{bad}'（声明头在语法上不容纳计算）", file, line));
                continue;
            }

            values[key] = value;
        }

        var kindText = values.GetValueOrDefault(FieldKind, "");
        ComputeUnitKind kind = ComputeUnitKind.Fn;
        var kindOk = false;
        switch (kindText)
        {
            case "expr": kind = ComputeUnitKind.Expr; kindOk = true; break;
            case "fn": kind = ComputeUnitKind.Fn; kindOk = true; break;
            case "proc": kind = ComputeUnitKind.Proc; kindOk = true; break;
            case "":
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    "缺少 kind 字段（expr|fn|proc）", file, line));
                break;
            default:
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"kind='{kindText}' 不受控（expr|fn|proc）", file, line));
                break;
        }

        var arguments = new List<ComputeArgument>();
        if (values.TryGetValue(FieldArgs, out var argsText))
        {
            foreach (var raw in argsText.Split(','))
            {
                var item = raw.Trim();
                if (item.Length == 0)
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                        "args 中存在空参数", file, line));
                    continue;
                }

                var colon = item.IndexOf(':');
                if (colon <= 0 || colon == item.Length - 1)
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                        $"参数 '{item}' 形状应为 name:type", file, line));
                    continue;
                }

                var name = item[..colon].Trim();
                var typeText = item[(colon + 1)..].Trim();
                if (!UsharpIdentifier.Matches(name))
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                        $"参数名 '{name}' 不是合法标识符", file, line));
                    continue;
                }

                if (!ComputeTypeParser.TryParse(typeText, out var type, out var typeError))
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                        $"参数 '{name}' 类型非法：{typeError}", file, line));
                    continue;
                }

                arguments.Add(new ComputeArgument(name, type));
            }

            var duplicate = arguments.GroupBy(a => a.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"参数名 '{duplicate.Key}' 重复", file, line));
            }
        }

        var hasReturn = false;
        var returnType = ComputeType.Unit;
        if (values.TryGetValue(FieldReturns, out var returnsText))
        {
            if (kind == ComputeUnitKind.Proc)
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    "kind=proc 不接受 returns（过程无返回值）", file, line));
            }
            else if (!ComputeTypeParser.TryParse(returnsText, out returnType, out var returnError))
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"returns 类型非法：{returnError}", file, line));
            }
            else
            {
                hasReturn = true;
            }
        }
        else if (kind is ComputeUnitKind.Expr or ComputeUnitKind.Fn)
        {
            errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                $"kind={kindText} 必须声明 returns", file, line));
        }

        ComputePurityPolicy purity = ComputePurityPolicy.Strict;
        var purityOk = false;
        switch (values.GetValueOrDefault(FieldPure, ""))
        {
            case "strict": purity = ComputePurityPolicy.Strict; purityOk = true; break;
            case "scoped": purity = ComputePurityPolicy.Scoped; purityOk = true; break;
            case "impure": purity = ComputePurityPolicy.Impure; purityOk = true; break;
            case "":
                errors.Add(new ComputeDiagnostic(ComputeCodes.PurityInvalid,
                    "缺少 pure 字段（strict|scoped|impure）", file, line));
                break;
            default:
                errors.Add(new ComputeDiagnostic(ComputeCodes.PurityInvalid,
                    $"pure='{values[FieldPure]}' 不受控（strict|scoped|impure）", file, line));
                break;
        }

        var effects = new List<ComputeEffect>();
        if (values.TryGetValue(FieldEffects, out var effectsText))
        {
            foreach (var raw in effectsText.Split(','))
            {
                var item = raw.Trim();
                ComputeEffect? effect = item switch
                {
                    "ks.read" => ComputeEffect.KeyRead,
                    "ks.write" => ComputeEffect.KeyWrite,
                    "emit" => ComputeEffect.Emit,
                    "clock" => ComputeEffect.Clock,
                    "random" => ComputeEffect.Random,
                    _ => null
                };

                if (effect is null)
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                        $"effects 中出现不受控效果 '{item}'（ks.read|ks.write|emit|clock|random）", file, line));
                    continue;
                }

                if (effects.Contains(effect.Value))
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                        $"effects 中效果 '{item}' 重复", file, line));
                    continue;
                }

                effects.Add(effect.Value);
            }
        }

        long? budget = null;
        if (values.TryGetValue(FieldBudget, out var budgetText))
        {
            var value = PositiveLong(budgetText, FieldBudget, errors, file, line, out var ok);
            if (ok) budget = value;
        }

        var slice = ComputeUnitHeader.DefaultSlice;
        if (values.TryGetValue(FieldSlice, out var sliceText))
        {
            var value = PositiveLong(sliceText, FieldSlice, errors, file, line, out var ok);
            if (ok) slice = value;
        }

        var heap = budget ?? ComputeUnitHeader.DefaultHeap;
        if (values.TryGetValue(FieldHeap, out var heapText))
        {
            var value = PositiveLong(heapText, FieldHeap, errors, file, line, out var ok);
            if (ok) heap = value;
        }

        var depth = budget ?? ComputeUnitHeader.DefaultDepth;
        if (values.TryGetValue(FieldDepth, out var depthText))
        {
            var value = PositiveLong(depthText, FieldDepth, errors, file, line, out var ok);
            if (ok) depth = value;
        }

        decimal? shareMax = null;
        if (values.TryGetValue(FieldShareMax, out var shareText))
        {
            if (!decimal.TryParse(shareText, NumberStyles.Number, CultureInfo.InvariantCulture, out var share)
                || share < 0m || share > 1m)
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"share_max='{shareText}' 不在 [0,1] 区间", file, line));
            }
            else
            {
                shareMax = share;
            }
        }

        ValidatePurity(purityOk, purity, effects, errors, file, line);

        if (errors.Count > 0 || !kindOk || !purityOk)
        {
            diagnostics = errors;
            return false;
        }

        header = new ComputeUnitHeader(
            declaration.Name, kind, arguments, returnType, hasReturn, purity, effects,
            budget, slice, heap, depth, shareMax, file, line);
        diagnostics = errors;
        return true;

        void AddEffect(ComputeEffect effect)
        {
            if (effects.Contains(effect))
            {
                errors.Add(new ComputeDiagnostic(ComputeCodes.HeaderInvalid,
                    $"effects 中效果 '{item}' 重复", file, line));
                return;
            }

            effects.Add(effect);
        }
    }

    private static void ValidatePurity(bool purityOk, ComputePurityPolicy purity,
        IReadOnlyList<ComputeEffect> effects, List<ComputeDiagnostic> errors, string file, int line)
    {
        if (!purityOk) return;
        switch (purity)
        {
            case ComputePurityPolicy.Strict when effects.Count > 0:
                errors.Add(new ComputeDiagnostic(ComputeCodes.PurityInvalid,
                    "pure=strict 不得声明 effects", file, line));
                break;
            case ComputePurityPolicy.Scoped:
                if (effects.Contains(ComputeEffect.Clock) || effects.Contains(ComputeEffect.Random))
                {
                    errors.Add(new ComputeDiagnostic(ComputeCodes.PurityInvalid,
                        "pure=scoped 不允许 clock/random（需要它们请用 pure=impure）", file, line));
                }

                break;
            default:
                break;
        }
    }

    private static long PositiveLong(string text, string field, List<ComputeDiagnostic> errors,
        string file, int line, out bool ok)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            errors.Add(new ComputeDiagnostic(ComputeCodes.BudgetInvalid,
                $"{field}='{text}' 必须为正整数", file, line));
            ok = false;
            return 0;
        }

        ok = true;
        return value;
    }

    private static string HeaderTail(DslDeclaration declaration)
    {
        var raw = declaration.RawLine.Trim();
        const string prefix = UsharpComputeClassifier.ComputePrefix;
        var rest = raw.StartsWith(prefix, StringComparison.Ordinal)
            ? raw[prefix.Length..].TrimStart()
            : raw;
        var colon = rest.IndexOf(':');
        return colon < 0 ? "" : rest[(colon + 1)..];
    }
}
