namespace Modules.Node.Spec;

/// <summary>
/// 读侧选择谓词（N7：裁剪只发生在读侧；写侧无裁剪副本）。
/// 声明式为极小表达式：<c>true</c> / <c>false</c> / <c>field=value</c> / <c>field!=value</c> / <c>field in (a;b)</c>。
/// </summary>
public interface ISelectionPredicate
{
    string SelectionId { get; }

    string Expression { get; }

    bool Accept(IReadOnlyDictionary<string, string> fields);
}

public sealed class ExpressionSelectionPredicate : ISelectionPredicate
{
    public const string AllSelectionId = "all";

    public ExpressionSelectionPredicate(string selectionId, string expression)
    {
        SelectionId = selectionId;
        Expression = string.IsNullOrWhiteSpace(expression) ? "true" : expression.Trim();
    }

    public string SelectionId { get; }

    public string Expression { get; }

    public static ExpressionSelectionPredicate All() => new(AllSelectionId, "true");

    public bool Accept(IReadOnlyDictionary<string, string> fields)
    {
        var expression = Expression;

        if (string.Equals(expression, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(expression, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expression.Contains(" in (", StringComparison.OrdinalIgnoreCase))
        {
            var open = expression.IndexOf('(', StringComparison.Ordinal);
            var close = expression.LastIndexOf(')');
            if (open < 0 || close < open)
            {
                return false;
            }

            var field = expression[..expression.IndexOf(" in (", StringComparison.OrdinalIgnoreCase)].Trim();
            var candidates = expression[(open + 1)..close]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return fields.TryGetValue(field, out var current) &&
                   candidates.Contains(current, StringComparer.Ordinal);
        }

        if (expression.Contains("!=", StringComparison.Ordinal))
        {
            var index = expression.IndexOf("!=", StringComparison.Ordinal);
            var field = expression[..index].Trim();
            var expected = expression[(index + 2)..].Trim();
            return !fields.TryGetValue(field, out var current) ||
                   !string.Equals(current, expected, StringComparison.Ordinal);
        }

        if (expression.Contains('='))
        {
            var index = expression.IndexOf('=');
            var field = expression[..index].Trim();
            var expected = expression[(index + 1)..].Trim();
            return fields.TryGetValue(field, out var current) &&
                   string.Equals(current, expected, StringComparison.Ordinal);
        }

        // 未识别表达式：不静默放行（门不合格 ⇒ ⊥ 语义）
        return false;
    }
}

/// <summary>选择集（声明装载；缺省含 <c>all</c>）。</summary>
public sealed class SelectionSet
{
    private readonly Dictionary<string, ISelectionPredicate> _selections = new(StringComparer.Ordinal);

    public SelectionSet()
    {
        Add(ExpressionSelectionPredicate.All());
    }

    public IReadOnlyCollection<string> Ids => _selections.Keys.ToArray();

    public void Add(ISelectionPredicate predicate) => _selections[predicate.SelectionId] = predicate;

    public bool TryGet(string selectionId, out ISelectionPredicate predicate)
    {
        if (_selections.TryGetValue(selectionId, out var found))
        {
            predicate = found;
            return true;
        }

        predicate = null!;
        return false;
    }

    public bool Accept(string selectionId, IReadOnlyDictionary<string, string> fields) =>
        TryGet(selectionId, out var predicate) && predicate.Accept(fields);
}
