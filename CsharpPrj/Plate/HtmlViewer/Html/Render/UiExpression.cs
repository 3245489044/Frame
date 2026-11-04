#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace UiBackend.Html;

/// <summary>表达式求值的旁路资源（文案表 / 格式化器）；由 <see cref="UiComponentCatalog"/> 提供。</summary>
public interface IUiExpressionResources
{
    /// <summary>文案查表：表名 + 键 → 文案；未命中返回 null（由表达式回退键名）。</summary>
    string? Label(string table, string key);

    /// <summary>格式化：格式化器名（别名已解析）+ 原值 → 文本；未知名返回 null。</summary>
    string? Format(string formatter, object? value);
}

/// <summary>
/// 白名单模型表达式求值（M6 最小算子）——只允许声明式派生，不执行脚本/函数调用。
/// <para>语法：字符串以 <c>$</c> 开头 = 模型引用（支持 <c>a.b</c> 路径）；否则为字面量；
/// 对象 = 单算子调用 <c>{ "op": [args…] }</c>。</para>
/// <para>算子：<c>eq / neq / and / or / not / notEmpty / isEmpty / coalesce / concat / format / lookup</c>。
/// 未知名/参数不足 → null（调用方保持基础模型，禁静默改写）。</para>
/// </summary>
public static class UiExpression
{
    public static object? Evaluate(
        object? expression,
        IReadOnlyDictionary<string, object?> model,
        IUiExpressionResources? resources = null)
    {
        if (expression is JsonElement element)
            return EvaluateElement(element, model, resources);
        return expression;
    }

    private static object? EvaluateElement(
        JsonElement element,
        IReadOnlyDictionary<string, object?> model,
        IUiExpressionResources? resources)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                return text.StartsWith('$') ? Lookup(model, text[1..]) : text;
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Select(x => EvaluateElement(x, model, resources))
                    .ToArray();
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    return EvaluateOperator(property.Name, property.Value, model, resources);
                return null;
            default:
                return null;
        }
    }

    private static object? EvaluateOperator(
        string op,
        JsonElement arg,
        IReadOnlyDictionary<string, object?> model,
        IUiExpressionResources? resources)
    {
        var args = EvaluateArgs(arg, model, resources);
        switch (op.ToLowerInvariant())
        {
            case "eq":
                return args.Count >= 2 && args.Skip(1).All(a => ScalarEquals(args[0], a));
            case "neq":
                return !(args.Count >= 2 && args.Skip(1).All(a => ScalarEquals(args[0], a)));
            case "and":
                return args.Count > 0 && args.All(Truthy);
            case "or":
                return args.Any(Truthy);
            case "not":
                return args.Count > 0 && !Truthy(args[0]);
            case "notempty":
                return args.Count > 0 && Truthy(args[0]);
            case "isempty":
                return args.Count == 0 || !Truthy(args[0]);
            case "coalesce":
                return args.FirstOrDefault(Truthy);
            case "concat":
                return string.Concat(args.Select(ToText));
            case "format":
                if (args.Count < 2) return null;
                var formatter = ToText(args[0]);
                var formatted = resources?.Format(formatter, args[1]);
                return formatted ?? args[1]?.ToString();
            case "lookup":
                if (args.Count < 2) return null;
                var table = ToText(args[0]);
                var key = ToText(args[1]);
                return resources?.Label(table, key) ?? key;
            default:
                return null;
        }
    }

    private static List<object?> EvaluateArgs(
        JsonElement arg,
        IReadOnlyDictionary<string, object?> model,
        IUiExpressionResources? resources)
        => arg.ValueKind == JsonValueKind.Array
            ? arg.EnumerateArray().Select(x => EvaluateElement(x, model, resources)).ToList()
            : new List<object?> { EvaluateElement(arg, model, resources) };

    /// <summary>模型路径取值（大小写不敏感；支持 <c>a.b</c> 嵌套字典）—— 单源 = <see cref="UiModel.Lookup"/>。</summary>
    private static object? Lookup(IReadOnlyDictionary<string, object?> model, string path)
        => UiModel.Lookup(model, path);

    private static bool ScalarEquals(object? left, object? right)
        => string.Equals(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);

    private static bool Truthy(object? value) => UiModel.Truthy(value);

    private static string ToText(object? value) => UiModel.ToText(value);
}
