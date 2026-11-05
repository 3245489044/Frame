using System;
using System.Collections.Generic;
using System.Text;

namespace Share.LogicComposition.Conversion.Validation.Rules;

/// <summary>谓词操作数：文本（路径 / 字面量 / 词表键）<b>或</b>集合表达式（取值器 / <c>union</c>）。</summary>
public sealed class PredicateOperand
{
    /// <summary>文本操作数（表达式操作数时为 null）。</summary>
    public string? Text { get; }

    /// <summary>表达式操作数（集合位置；非表达式时为 null）。</summary>
    public PredicateExpr? Expr { get; }

    /// <summary>是否表达式操作数。</summary>
    public bool IsExpr => Expr is not null;

    private PredicateOperand(string? text, PredicateExpr? expr)
    {
        Text = text;
        Expr = expr;
    }

    /// <summary>文本操作数（路径 / 字面量 / 词表键）。</summary>
    /// <param name="text">文本。</param>
    public static PredicateOperand Of(string text) => new PredicateOperand(text ?? string.Empty, null);

    /// <summary>表达式操作数（集合表达式）。</summary>
    /// <param name="expr">集合表达式。</param>
    public static PredicateOperand Of(PredicateExpr expr)
        => new PredicateOperand(null, expr ?? throw new ArgumentNullException(nameof(expr)));

    /// <summary>规范化文本。</summary>
    public override string ToString() => IsExpr ? Expr!.ToString() : Text ?? string.Empty;
}

/// <summary>
/// 封闭谓词表达式树节点：规则表判据的唯一值形态（<c>RuleSpec.Require</c> 的类型）。
/// <para>
/// <b>封闭性</b>：节点类型必须命中 <see cref="PredicateCatalog"/> 白名单；操作数按**槽位角色**逐一校验
/// （<see cref="PredicateRole"/>：路径 / 路径或字面量 / 词表键 / 集合）——未知即**硬失败**，绝不降级为空转。
/// 无变量 / 无循环 / 无宏 / 无条件关键字。
/// </para>
/// <para>本类型只承载**结构**，不求值：求值由唯一解释内核 <see cref="PredicateEvaluator{TModel}"/> 完成。</para>
/// </summary>
public sealed class PredicateExpr
{
    private static readonly IReadOnlyList<PredicateExpr> EmptyChildren = Array.Empty<PredicateExpr>();
    private static readonly IReadOnlyList<PredicateOperand> EmptyOperands = Array.Empty<PredicateOperand>();
    private static readonly IReadOnlyDictionary<string, string> EmptyNamed =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    /// <summary>集合表达式节点类型（可占 <see cref="PredicateRole.Set"/> 槽位的表达式）。</summary>
    public static readonly IReadOnlyList<string> SetExpressionKinds = new[]
    {
        PredicateCatalog.Union,
        PredicateCatalog.DeclaredStates,
        PredicateCatalog.DeclaredModules,
        PredicateCatalog.ChainDerived,
        PredicateCatalog.PriorKnowledge,
        PredicateCatalog.Link,
    };

    /// <summary>节点类型（白名单键；见 <see cref="PredicateCatalog"/>）。</summary>
    public string Kind { get; }

    /// <summary>操作数（**位置即语义**；角色由白名单条目给出）。</summary>
    public IReadOnlyList<PredicateOperand> Operands { get; }

    /// <summary>子表达式（谓词代数节点 <c>all-of</c>/<c>any-of</c>/<c>not</c>）。</summary>
    public IReadOnlyList<PredicateExpr> Children { get; }

    /// <summary>数值实参（<c>count-gt(path, n)</c> 的 <c>n</c>）。</summary>
    public int? Number { get; }

    /// <summary>具名实参（<c>link(type=perception)</c> 的 <c>type</c>）。</summary>
    public IReadOnlyDictionary<string, string> NamedArgs { get; }

    /// <summary>构筑谓词节点（形状非法由 <c>TryValidate</c> 报出，不在构造期抛）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="operands">操作数序。</param>
    /// <param name="children">子表达式。</param>
    /// <param name="namedArgs">具名实参。</param>
    /// <param name="number">数值实参。</param>
    public PredicateExpr(
        string kind,
        IEnumerable<PredicateOperand>? operands = null,
        IEnumerable<PredicateExpr>? children = null,
        IEnumerable<KeyValuePair<string, string>>? namedArgs = null,
        int? number = null)
    {
        Kind = kind ?? string.Empty;
        Operands = operands is null ? EmptyOperands : new List<PredicateOperand>(operands);
        Children = children is null ? EmptyChildren : new List<PredicateExpr>(children);
        Number = number;

        if (namedArgs is null)
        {
            NamedArgs = EmptyNamed;
        }
        else
        {
            var named = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in namedArgs) named[pair.Key] = pair.Value;
            NamedArgs = named;
        }
    }

    /// <summary>文本操作数槽的节点（如 <c>present(a)</c> / <c>unique(modules, id)</c>）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="operands">文本操作数。</param>
    public static PredicateExpr Of(string kind, params string[] operands)
    {
        var list = new List<PredicateOperand>();
        if (operands is not null)
        {
            for (var i = 0; i < operands.Length; i++) list.Add(PredicateOperand.Of(operands[i]));
        }

        return new PredicateExpr(kind, list);
    }

    /// <summary>带数值实参的节点（<c>count-gt(path, n)</c>）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="number">数值实参。</param>
    /// <param name="operands">文本操作数。</param>
    public static PredicateExpr WithNumber(string kind, int number, params string[] operands)
    {
        var draft = Of(kind, operands);
        return new PredicateExpr(kind, draft.Operands, number: number);
    }

    /// <summary>带具名实参的节点（<c>link(type=…)</c>）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="key">具名键。</param>
    /// <param name="value">具名值。</param>
    public static PredicateExpr WithNamed(string kind, string key, string value)
        => new PredicateExpr(kind, namedArgs: new[] { new KeyValuePair<string, string>(key, value) });

    /// <summary>谓词代数组合节点（<c>all-of</c>/<c>any-of</c>/<c>not</c>）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="children">子表达式。</param>
    public static PredicateExpr Combine(string kind, params PredicateExpr[] children)
        => new PredicateExpr(kind, children: children);

    /// <summary>取值器节点（集合表达式；<c>chain-derived(to)</c> 用 <paramref name="arg"/>）。</summary>
    /// <param name="kind">取值器类型。</param>
    /// <param name="arg">可选实参（<c>chain-derived</c> 的 <c>to</c>）。</param>
    public static PredicateExpr Provider(string kind, string? arg = null)
        => arg is null
            ? new PredicateExpr(kind)
            : new PredicateExpr(kind, new[] { PredicateOperand.Of(arg) });

    /// <summary>两操作数集合节点（<c>union</c>/<c>intersects</c>）。</summary>
    /// <param name="kind">节点类型。</param>
    /// <param name="left">左操作数（路径文本或集合表达式）。</param>
    /// <param name="right">右操作数。</param>
    public static PredicateExpr Set(string kind, PredicateOperand left, PredicateOperand right)
        => new PredicateExpr(kind, new[] { left, right });

    /// <summary>是否为集合表达式节点类型。</summary>
    /// <param name="kind">节点类型。</param>
    public static bool IsSetExpressionKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return false;

        for (var i = 0; i < SetExpressionKinds.Count; i++)
        {
            if (string.Equals(SetExpressionKinds[i], kind, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// 白名单形状校验（递归）：非法返回 false 并给出**可定位**原因（路径形如 <c>require[1].present</c>）。
    /// 调用方（<c>RuleSpecEngine.Build</c>）按「构筑期硬失败」处理，不走运行期诊断流。
    /// </summary>
    /// <param name="error">out：失败原因（成功为 null）。</param>
    public bool TryValidate(out string? error) => TryValidate("require", out error);

    /// <summary>自身与全部后代（供构筑期注册校验遍历）。</summary>
    public IEnumerable<PredicateExpr> SelfAndDescendants()
    {
        yield return this;

        for (var i = 0; i < Operands.Count; i++)
        {
            if (Operands[i].IsExpr)
            {
                foreach (var node in Operands[i].Expr!.SelfAndDescendants()) yield return node;
            }
        }

        for (var i = 0; i < Children.Count; i++)
        {
            foreach (var node in Children[i].SelfAndDescendants()) yield return node;
        }
    }

    /// <summary>规范化表达式文本（稳定形态，供构筑期消息与测试断言）。</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Append(builder);
        return builder.ToString();
    }

    private bool TryValidate(string path, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(Kind))
        {
            error = path + "：节点类型为空";
            return false;
        }

        if (PredicateCatalog.IsReservedKeyword(Kind))
        {
            error = path + "：节点类型 " + Kind + " 属受控控制流关键字（封闭白名单禁止）";
            return false;
        }

        if (!PredicateCatalog.TryGetSpec(Kind, out var spec))
        {
            error = path + "：未知节点类型 " + Kind + "（白名单：" + string.Join("/", PredicateCatalog.OrderedKinds) + "）";
            return false;
        }

        if (Operands.Count != spec.OperandCount)
        {
            error = path + "：" + Kind + " 需要 " + spec.OperandCount + " 个操作数（实得 " + Operands.Count
                + "；槽位角色：" + string.Join(", ", spec.OperandRoles) + "）";
            return false;
        }

        for (var i = 0; i < Operands.Count; i++)
        {
            if (Operands[i] is null)
            {
                error = path + "[" + i + "]：操作数为空";
                return false;
            }

            if (!ValidateOperand(i, spec.RoleAt(i)!, path, out error)) return false;
        }

        if (Children.Count < spec.MinChildren || Children.Count > spec.MaxChildren)
        {
            error = path + "：" + Kind + " 子表达式数非法（实得 " + Children.Count
                + "，允许 " + DescribeArity(spec.MinChildren, spec.MaxChildren) + "）";
            return false;
        }

        if (spec.RequiresNumber && Number is null)
        {
            error = path + "：" + Kind + " 缺数值实参（n）";
            return false;
        }

        if (!spec.RequiresNumber && Number is not null)
        {
            error = path + "：" + Kind + " 不接受数值实参";
            return false;
        }

        for (var i = 0; i < spec.NamedArgKeys.Count; i++)
        {
            var key = spec.NamedArgKeys[i];
            if (!NamedArgs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                error = path + "：" + Kind + " 缺具名实参 " + key + "（该节点声明的具名键为必给）";
                return false;
            }
        }

        foreach (var pair in NamedArgs)
        {
            if (!Contains(spec.NamedArgKeys, pair.Key))
            {
                error = path + "：" + Kind + " 未知具名实参 " + pair.Key;
                return false;
            }
        }

        for (var i = 0; i < Children.Count; i++)
        {
            if (Children[i] is null)
            {
                error = path + "[" + i + "]：子表达式为空";
                return false;
            }

            if (!Children[i].TryValidate(path + "[" + i + "]", out error)) return false;
        }

        return true;
    }

    private bool ValidateOperand(int index, string role, string path, out string? error)
    {
        error = null;
        var slot = path + "[" + index + "]";
        var operand = Operands[index];

        switch (role)
        {
            case PredicateRole.Path:
                if (operand.IsExpr)
                {
                    error = slot + "：path 槽位不接受集合表达式";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(operand.Text))
                {
                    error = slot + "：路径为空";
                    return false;
                }

                if (!RulePath.TryParse(operand.Text, out _, out var reason))
                {
                    error = slot + "：路径非法：" + reason;
                    return false;
                }

                return true;

            case PredicateRole.PathOrLiteral:
                if (operand.IsExpr)
                {
                    error = slot + "：path|literal 槽位不接受集合表达式";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(operand.Text))
                {
                    error = slot + "：右式为空（须为路径或字面量）";
                    return false;
                }

                return true;

            case PredicateRole.Whitelist:
                if (operand.IsExpr || string.IsNullOrWhiteSpace(operand.Text))
                {
                    error = slot + "：词表键为空（须为已注册词表键）";
                    return false;
                }

                return true;

            default:
                return ValidateSetOperand(index, role, path, out error);
        }
    }

    private bool ValidateSetOperand(int index, string role, string path, out string? error)
    {
        error = null;
        var slot = path + "[" + index + "]";
        var operand = Operands[index];

        if (operand.IsExpr)
        {
            var expr = operand.Expr!;
            if (!IsSetExpressionKind(expr.Kind))
            {
                error = slot + "：集合槽位只接受取值器或 union（实得 " + expr.Kind + "）";
                return false;
            }

            return expr.TryValidate(slot, out error);
        }

        if (role == PredicateRole.SetExpr)
        {
            error = slot + "：该槽位须为集合表达式（取值器 / union）";
            return false;
        }

        if (string.IsNullOrWhiteSpace(operand.Text))
        {
            error = slot + "：集合实参为空（须为路径或集合表达式）";
            return false;
        }

        if (!RulePath.TryParse(operand.Text, out _, out var reason))
        {
            error = slot + "：集合实参路径非法：" + reason;
            return false;
        }

        return true;
    }

    private void Append(StringBuilder builder)
    {
        builder.Append(Kind);

        if (Operands.Count > 0 || Number is not null || NamedArgs.Count > 0)
        {
            builder.Append('(');
            var first = true;
            for (var i = 0; i < Operands.Count; i++)
            {
                if (!first) builder.Append(", ");
                first = false;
                AppendOperand(builder, Operands[i]);
            }

            if (Number is not null)
            {
                if (!first) builder.Append(", ");
                first = false;
                builder.Append(Number.Value);
            }

            foreach (var pair in NamedArgs)
            {
                if (!first) builder.Append(", ");
                first = false;
                builder.Append(pair.Key).Append('=').Append(pair.Value);
            }

            builder.Append(')');
        }

        if (Children.Count > 0)
        {
            builder.Append('[');
            for (var i = 0; i < Children.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                Children[i].Append(builder);
            }

            builder.Append(']');
        }
    }

    private static void AppendOperand(StringBuilder builder, PredicateOperand operand)
    {
        if (operand.IsExpr) operand.Expr!.Append(builder);
        else builder.Append(operand.Text);
    }

    private static string DescribeArity(int min, int max)
        => max == int.MaxValue ? "至少 " + min : min == max ? min.ToString() : min + ".." + max;

    private static bool Contains(IReadOnlyList<string> items, string value)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], value, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
