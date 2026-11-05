using System;
using System.Collections.Generic;
using System.Globalization;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>
/// 谓词求值器（**唯一解释内核**）：把 <see cref="PredicateExpr"/> 在「作用域节点 + 领域模型」上求值为真/假。
/// <para>
/// 双模式：<b>布尔位置</b>（判据成立与否）与<b>集合位置</b>（<see cref="PredicateRole.Set"/> 槽位：
/// 取值器 / <c>union</c>，供 <c>ref-declared</c> / <c>intersects</c> 消费）。集合节点落到布尔位置时取「非空即真」。
/// </para>
/// <para>
/// <b>缺失口径（明确）</b>：取值缺失一律**不判违规**（返回 true）——缺失由 <c>present</c>/<c>absent</c>
/// 类判据显式负责；本类不抛异常、不静默改判（保持「全量失败可见」）。
/// </para>
/// </summary>
/// <typeparam name="TModel">领域模型。</typeparam>
public sealed class PredicateEvaluator<TModel>
{
    private static readonly IReadOnlyList<string> NoValues = Array.Empty<string>();
    private static readonly IReadOnlyList<ConversionNode> NoNodes = Array.Empty<ConversionNode>();

    private readonly IRuleObjectModel<TModel> _model;
    private readonly IPredicateRegistry<TModel> _registry;

    /// <summary>构筑求值器（端口与注册表不得为 null）。</summary>
    /// <param name="model">取值端口。</param>
    /// <param name="registry">受控注册表。</param>
    public PredicateEvaluator(IRuleObjectModel<TModel> model, IPredicateRegistry<TModel> registry)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>布尔位置求值（判据成立 = true）。</summary>
    /// <param name="expr">谓词表达式。</param>
    /// <param name="model">领域模型。</param>
    /// <param name="scopeNode">当前作用域节点。</param>
    public bool Evaluate(PredicateExpr expr, TModel model, ConversionNode? scopeNode)
    {
        if (expr is null) return true;

        switch (expr.Kind)
        {
            case PredicateCatalog.AllOf:
                return AllMatch(expr, model, scopeNode, expected: true);

            case PredicateCatalog.AnyOf:
                return AllMatch(expr, model, scopeNode, expected: false);

            case PredicateCatalog.Not:
                return !Evaluate(expr.Children[0], model, scopeNode);

            case PredicateCatalog.Present:
                return Resolve(expr.Operands[0], scopeNode).Count > 0;

            case PredicateCatalog.Absent:
                return Resolve(expr.Operands[0], scopeNode).Count == 0;

            case PredicateCatalog.NonEmpty:
                return IsAnyNonEmpty(Resolve(expr.Operands[0], scopeNode));

            case PredicateCatalog.Empty:
                return !IsAnyNonEmpty(Resolve(expr.Operands[0], scopeNode));

            case PredicateCatalog.CountGt:
                return CountElements(expr.Operands[0], scopeNode) > (expr.Number ?? 0);

            case PredicateCatalog.Equals_:
                return EvaluateEquals(expr, model, scopeNode);

            case PredicateCatalog.EnumIn:
                return EvaluateEnumIn(expr, scopeNode);

            case PredicateCatalog.KeyInWhitelist:
                return EvaluateKeyInWhitelist(expr, scopeNode);

            case PredicateCatalog.Unique:
                return EvaluateUnique(expr, scopeNode);

            case PredicateCatalog.Intersects:
                return Intersect(SetOf(expr.Operands[0], model, scopeNode), SetOf(expr.Operands[1], model, scopeNode)).Count > 0;

            case PredicateCatalog.RefDeclared:
                return EvaluateRefDeclared(expr, model, scopeNode);

            case PredicateCatalog.HasEffect:
                return _registry.Evaluate(PredicateCatalog.HasEffect, model, scopeNode, expr.Operands[0].Text ?? string.Empty);

            default:
                // 取值器 / union 落到布尔位置：非空即真（口径见类型注释）。
                return SetOf(expr.Operands.Count > 0 ? expr.Operands[0] : null, model, scopeNode).Count > 0;
        }
    }

    private bool AllMatch(PredicateExpr expr, TModel model, ConversionNode? scopeNode, bool expected)
    {
        for (var i = 0; i < expr.Children.Count; i++)
        {
            if (Evaluate(expr.Children[i], model, scopeNode) != expected) return !expected;
        }

        return expected;
    }

    // ---- 叶子判据 ----

    private bool EvaluateEquals(PredicateExpr expr, TModel model, ConversionNode? scopeNode)
    {
        var left = Resolve(expr.Operands[0], scopeNode);
        if (left.Count == 0) return true; // 缺失不判违规（由 present/absent 负责）

        var rightOperand = expr.Operands[1];
        if (!rightOperand.IsExpr && RulePath.TryParse(rightOperand.Text, out var path, out _))
        {
            // 右式跨层引用口径：**先按当前作用域解析，未命中回落模型根**（故 `modules[*]` 作用域内
            // 可引用 `meta.driving_force` 这类根相对路径）；两处皆未命中才按字面量比较。
            var right = path.ResolveAll(scopeNode);
            if (right.Count == 0)
            {
                var root = _model.Root(model);
                if (!ReferenceEquals(root, scopeNode)) right = path.ResolveAll(root);
            }

            if (right.Count > 0) return string.Equals(TextOf(left[0]), TextOf(right[0]), StringComparison.Ordinal);
        }

        return string.Equals(TextOf(left[0]), rightOperand.Text, StringComparison.Ordinal);
    }

    private bool EvaluateEnumIn(PredicateExpr expr, ConversionNode? scopeNode)
    {
        var values = TextsOf(Resolve(expr.Operands[0], scopeNode));
        if (values.Count == 0) return true;

        var key = expr.Operands[1].Text ?? string.Empty;
        if (!_registry.TryGetWhitelist(key, out var whitelist)) return true;

        for (var i = 0; i < values.Count; i++)
        {
            if (!Contains(whitelist, values[i])) return false; // 非白名单项 = 违规
        }

        return true;
    }

    private bool EvaluateKeyInWhitelist(PredicateExpr expr, ConversionNode? scopeNode)
    {
        var holders = Resolve(expr.Operands[0], scopeNode);
        if (holders.Count == 0) return true;

        var key = expr.Operands[1].Text ?? string.Empty;
        if (!_registry.TryGetWhitelist(key, out var whitelist)) return true;

        for (var i = 0; i < holders.Count; i++)
        {
            if (!KeysWithin(holders[i], whitelist)) return false;
        }

        return true;
    }

    private bool EvaluateUnique(PredicateExpr expr, ConversionNode? scopeNode)
    {
        var elements = ElementsOf(expr.Operands[0], scopeNode);
        if (elements.Count < 2) return true;

        if (!RulePath.TryParse(expr.Operands[1].Text, out var keyPath, out _)) return true;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < elements.Count; i++)
        {
            if (!keyPath.TryResolve(elements[i], out var key) || key is null) continue;

            var text = TextOf(key);
            if (text is null) continue;
            if (!seen.Add(text)) return false; // 重复键 = 违规
        }

        return true;
    }

    private bool EvaluateRefDeclared(PredicateExpr expr, TModel model, ConversionNode? scopeNode)
    {
        var declared = SetOf(expr.Operands[1], model, scopeNode);
        if (declared.Count == 0) return true;

        var referenced = TextsOf(Resolve(expr.Operands[0], scopeNode));
        for (var i = 0; i < referenced.Count; i++)
        {
            if (!Contains(declared, referenced[i])) return false; // 引用未声明 = 违规
        }

        return true;
    }

    // ---- 取值 ----

    /// <summary>解释集合槽位（表达式 → 取值器 / union；文本 → 路径展平）。</summary>
    private IReadOnlyList<string> SetOf(PredicateOperand? operand, TModel model, ConversionNode? scopeNode)
    {
        if (operand is null) return NoValues;

        if (operand.IsExpr)
        {
            var expr = operand.Expr!;
            switch (expr.Kind)
            {
                case PredicateCatalog.Union:
                    return Union(SetOf(expr.Operands[0], model, scopeNode), SetOf(expr.Operands[1], model, scopeNode));

                case PredicateCatalog.ChainDerived:
                    return _registry.ResolveValues(expr.Kind, model, scopeNode, expr.Operands[0].Text);

                case PredicateCatalog.Link:
                    expr.NamedArgs.TryGetValue("type", out var linkType);
                    return _registry.ResolveValues(expr.Kind, model, scopeNode, linkType);

                default:
                    return PredicateExpr.IsSetExpressionKind(expr.Kind)
                        ? _registry.ResolveValues(expr.Kind, model, scopeNode, null)
                        : TextsOf(ResolveOperandPath(expr.Operands.Count > 0 ? expr.Operands[0] : null, scopeNode));
            }
        }

        return TextsOf(Resolve(operand, scopeNode));
    }

    private IReadOnlyList<ConversionNode> ResolveOperandPath(PredicateOperand? operand, ConversionNode? scopeNode)
        => operand is null ? NoNodes : Resolve(operand, scopeNode);

    private IReadOnlyList<ConversionNode> Resolve(PredicateOperand operand, ConversionNode? scopeNode)
        => operand.IsExpr || !RulePath.TryParse(operand.Text, out var path, out _) ? NoNodes : path.ResolveAll(scopeNode);

    private IReadOnlyList<ConversionNode> ElementsOf(PredicateOperand operand, ConversionNode? scopeNode)
        => operand.IsExpr ? NoNodes : _model.Enumerate(scopeNode, operand.Text ?? string.Empty);

    private int CountElements(PredicateOperand operand, ConversionNode? scopeNode) => ElementsOf(operand, scopeNode).Count;

    // ---- 通用取值/集合原语 ----

    private static bool IsAnyNonEmpty(IReadOnlyList<ConversionNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!IsEmptyNode(nodes[i])) return true;
        }

        return false;
    }

    private static bool IsEmptyNode(ConversionNode node)
    {
        switch (node)
        {
            case null:
                return true;

            case ConversionScalar scalar:
                switch (scalar.ScalarKind)
                {
                    case ConversionScalarKind.Null:
                        return true;
                    case ConversionScalarKind.String:
                        return string.IsNullOrEmpty(scalar.Value as string);
                    default:
                        return false;
                }

            case ConversionArray array:
                return array.Count == 0;

            case ConversionObject obj:
                return obj.Count == 0;

            default:
                return false;
        }
    }

    private static string? TextOf(ConversionNode? node)
    {
        if (node is not ConversionScalar scalar) return null;

        switch (scalar.ScalarKind)
        {
            case ConversionScalarKind.Null:
                return null;
            case ConversionScalarKind.String:
                return scalar.Value as string;
            case ConversionScalarKind.Boolean:
                return scalar.Value is bool flag && flag ? "true" : "false";
            case ConversionScalarKind.Integer:
                return scalar.Value is long number ? number.ToString(CultureInfo.InvariantCulture) : null;
            default:
                return scalar.Value is double value ? value.ToString("R", CultureInfo.InvariantCulture) : null;
        }
    }

    /// <summary>节点集 → 文本集（标量取文本；数组取其中标量元素文本；其余跳过）。</summary>
    private static IReadOnlyList<string> TextsOf(IReadOnlyList<ConversionNode> nodes)
    {
        if (nodes.Count == 0) return NoValues;

        var values = new List<string>();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is ConversionArray array)
            {
                for (var n = 0; n < array.Items.Count; n++)
                {
                    var text = TextOf(array.Items[n]);
                    if (text is not null) values.Add(text);
                }

                continue;
            }

            var single = TextOf(nodes[i]);
            if (single is not null) values.Add(single);
        }

        return values;
    }

    /// <summary>对象键须落在白名单内（数组逐项下钻；标量/缺失视为合规）。</summary>
    private static bool KeysWithin(ConversionNode node, IReadOnlyList<string> whitelist)
    {
        switch (node)
        {
            case ConversionObject obj:
                for (var i = 0; i < obj.Members.Count; i++)
                {
                    if (!Contains(whitelist, obj.Members[i].Key)) return false;
                }

                return true;

            case ConversionArray array:
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (!KeysWithin(array.Items[i], whitelist)) return false;
                }

                return true;

            default:
                return true;
        }
    }

    private static IReadOnlyList<string> Union(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 && right.Count == 0) return NoValues;

        var merged = new List<string>();
        for (var i = 0; i < left.Count; i++)
        {
            if (!merged.Contains(left[i])) merged.Add(left[i]);
        }

        for (var i = 0; i < right.Count; i++)
        {
            if (!merged.Contains(right[i])) merged.Add(right[i]);
        }

        return merged;
    }

    private static IReadOnlyList<string> Intersect(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return NoValues;

        var common = new List<string>();
        for (var i = 0; i < left.Count; i++)
        {
            if (Contains(right, left[i]) && !common.Contains(left[i])) common.Add(left[i]);
        }

        return common;
    }

    private static bool Contains(IReadOnlyList<string> items, string value)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], value, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}

