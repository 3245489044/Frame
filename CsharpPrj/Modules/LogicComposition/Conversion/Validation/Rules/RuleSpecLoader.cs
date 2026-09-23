using System;
using System.Collections.Generic;
using Share.LogicComposition.Conversion.Config;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 规则表装载器：JSON 文本 → <see cref="RuleSpec"/> 列表（复用内核 <see cref="GeneratorConfigReader"/> 的
/// JSON→IR 骨架，不引入第二套解析器）。
/// <para>
/// 规则表形态（键序稳定、可 diff）：
/// <code>
/// { "rules": [
///     { "name": "syntax.version", "code": "NM1001", "severity": "Error", "scope": "root",
///       "require": { "kind": "non-empty", "operands": ["nmd"] },
///       "message": "缺少 nmd 段", "spanHint": "nmd" } ] }
/// </code>
/// 谓词 JSON 与 <see cref="PredicateExpr"/> 同构：<c>kind</c> / <c>operands</c>（字符串或嵌套谓词对象）/
/// <c>children</c> / <c>named</c> / <c>number</c>。
/// </para>
/// <para>装载失败（JSON 非法 / 形状非法）返回 false + 原因；**语义级**校验（白名单 / 码段 / 注册项）
/// 留给 <see cref="RuleSpecEngine.Build"/>（构筑期硬失败）。</para>
/// </summary>
public static class RuleSpecLoader
{
    /// <summary>装载规则表。</summary>
    /// <param name="path">文件路径（仅用于错误描述）。</param>
    /// <param name="text">规则表 JSON 文本。</param>
    /// <param name="specs">out：规则列表（失败为 null）。</param>
    /// <param name="error">out：失败原因。</param>
    public static bool TryLoad(string path, string text, out IReadOnlyList<RuleSpec>? specs, out string? error)
    {
        specs = null;
        error = null;

        if (!GeneratorConfigReader.TryRead(path, text, out var root, out error) || root is null) return false;
        if (!TryGetRulesArray(root, out var rows, out error)) return false;

        var list = new List<RuleSpec>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not ConversionObject row)
            {
                error = path + "：rules[" + i + "] 须为对象";
                return false;
            }

            if (!TryReadSpec(path, i, row, out var spec, out error)) return false;
            list.Add(spec);
        }

        specs = list;
        return true;
    }

    private static bool TryGetRulesArray(ConversionObject root, out IReadOnlyList<ConversionNode> rows, out string? error)
    {
        rows = Array.Empty<ConversionNode>();
        error = null;

        if (!root.TryGet("rules", out var node))
        {
            error = "规则表缺 rules 数组";
            return false;
        }

        if (node is not ConversionArray array)
        {
            error = "rules 须为数组";
            return false;
        }

        rows = array.Items;
        return true;
    }

    private static bool TryReadSpec(
        string path, int index, ConversionObject row, out RuleSpec spec, out string? error)
    {
        spec = null!;
        error = null;

        var where = path + "：rules[" + index + "]";
        var name = row.GetString("name");
        var code = row.GetString("code");
        var severityText = row.GetString("severity");
        var scopeText = row.GetString("scope");
        var message = row.GetString("message") ?? string.Empty;
        var spanHint = row.GetString("spanHint");

        if (string.IsNullOrWhiteSpace(name))
        {
            error = where + " 缺 name";
            return false;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            error = where + "（" + name + "）缺 code";
            return false;
        }

        if (!TryParseSeverity(severityText, out var severity))
        {
            error = where + "（" + name + "）severity 非法（合法：Error|Warning|Info）";
            return false;
        }

        if (!RuleSpecScope.TryParse(scopeText, out var scope, out var scopeReason))
        {
            error = where + "（" + name + "）scope 非法：" + scopeReason;
            return false;
        }

        if (!row.TryGet("require", out var requireNode) || requireNode is not ConversionObject requireObject)
        {
            error = where + "（" + name + "）缺 require 对象";
            return false;
        }

        if (!TryReadExpr(where, name!, requireObject, out var require, out error)) return false;

        spec = new RuleSpec(name!, code!, severity, scope, require, message, spanHint);
        return true;
    }

    private static bool TryParseSeverity(string? text, out ConversionSeverity severity)
    {
        severity = ConversionSeverity.Error;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (string.Equals(text, "Error", StringComparison.OrdinalIgnoreCase))
        {
            severity = ConversionSeverity.Error;
            return true;
        }

        if (string.Equals(text, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            severity = ConversionSeverity.Warning;
            return true;
        }

        if (string.Equals(text, "Info", StringComparison.OrdinalIgnoreCase))
        {
            severity = ConversionSeverity.Info;
            return true;
        }

        return false;
    }

    private static bool TryReadExpr(
        string where, string ruleName, ConversionObject node, out PredicateExpr expr, out string? error)
    {
        expr = null!;
        error = null;

        var kind = node.GetString("kind");
        if (string.IsNullOrWhiteSpace(kind))
        {
            error = where + "（" + ruleName + "）谓词缺 kind";
            return false;
        }

        var operands = new List<PredicateOperand>();
        if (node.TryGet("operands", out var operandsNode))
        {
            if (operandsNode is not ConversionArray operandArray)
            {
                error = where + "（" + ruleName + "）" + kind + ".operands 须为数组";
                return false;
            }

            for (var i = 0; i < operandArray.Items.Count; i++)
            {
                var item = operandArray.Items[i];
                if (item is ConversionScalar { ScalarKind: ConversionScalarKind.String } scalar)
                {
                    operands.Add(PredicateOperand.Of((string?)scalar.Value ?? string.Empty));
                    continue;
                }

                if (item is ConversionObject nested)
                {
                    if (!TryReadExpr(where, ruleName, nested, out var nestedExpr, out error)) return false;
                    operands.Add(PredicateOperand.Of(nestedExpr));
                    continue;
                }

                error = where + "（" + ruleName + "）" + kind + ".operands[" + i + "] 须为字符串或谓词对象";
                return false;
            }
        }

        var children = new List<PredicateExpr>();
        if (node.TryGet("children", out var childrenNode))
        {
            if (childrenNode is not ConversionArray childArray)
            {
                error = where + "（" + ruleName + "）" + kind + ".children 须为数组";
                return false;
            }

            for (var i = 0; i < childArray.Items.Count; i++)
            {
                if (childArray.Items[i] is not ConversionObject child)
                {
                    error = where + "（" + ruleName + "）" + kind + ".children[" + i + "] 须为谓词对象";
                    return false;
                }

                if (!TryReadExpr(where, ruleName, child, out var childExpr, out error)) return false;
                children.Add(childExpr);
            }
        }

        var named = new List<KeyValuePair<string, string>>();
        if (node.TryGet("named", out var namedNode))
        {
            if (namedNode is not ConversionObject namedObject)
            {
                error = where + "（" + ruleName + "）" + kind + ".named 须为对象";
                return false;
            }

            foreach (var member in namedObject.Members)
            {
                named.Add(new KeyValuePair<string, string>(member.Key, ScalarText(member.Value)));
            }
        }

        int? number = null;
        if (node.TryGet("number", out var numberNode)
            && numberNode is ConversionScalar { ScalarKind: ConversionScalarKind.Integer } numberScalar
            && numberScalar.Value is long value)
        {
            number = (int)value;
        }

        expr = new PredicateExpr(kind, operands, children, named, number);
        return true;
    }

    private static string ScalarText(ConversionNode node)
        => node is ConversionScalar scalar ? scalar.Value?.ToString() ?? string.Empty : string.Empty;
}
