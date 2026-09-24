using Modules.Node.Diagnostics;

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

/// <summary>
/// 表达式选择谓词：解析结果<b>一次编译缓存</b>（逐条目 Accept 调用，禁每次重解析字符串），
/// 并把"是否可识别"显式化（<see cref="IsRecognized"/>），供 <see cref="SelectionSet"/> 产可见化诊断。
/// </summary>
public sealed class ExpressionSelectionPredicate : ISelectionPredicate
{
    public const string AllSelectionId = "all";

    private readonly Lazy<(bool Recognized, Func<IReadOnlyDictionary<string, string>, bool> Evaluate)> _compiled;

    public ExpressionSelectionPredicate(string selectionId, string expression)
    {
        SelectionId = selectionId;
        Expression = string.IsNullOrWhiteSpace(expression) ? "true" : expression.Trim();
        _compiled = new Lazy<(bool, Func<IReadOnlyDictionary<string, string>, bool>)>(() => Compile(Expression));
    }

    public string SelectionId { get; }

    public string Expression { get; }

    /// <summary>表达式是否落入声明允许的形态；<c>false</c> ⇒ 判据不合格（⊥），须可见化。</summary>
    public bool IsRecognized => _compiled.Value.Recognized;

    public static ExpressionSelectionPredicate All() => new(AllSelectionId, "true");

    public bool Accept(IReadOnlyDictionary<string, string> fields) => _compiled.Value.Evaluate(fields);

    private static (bool Recognized, Func<IReadOnlyDictionary<string, string>, bool> Evaluate) Compile(string expression)
    {
        if (string.Equals(expression, "true", StringComparison.OrdinalIgnoreCase))
        {
            return (true, _ => true);
        }

        if (string.Equals(expression, "false", StringComparison.OrdinalIgnoreCase))
        {
            return (true, _ => false);
        }

        if (expression.Contains(" in (", StringComparison.OrdinalIgnoreCase))
        {
            var open = expression.IndexOf('(', StringComparison.Ordinal);
            var close = expression.LastIndexOf(')');
            if (open < 0 || close < open)
            {
                return Unrecognized;
            }

            var field = expression[..expression.IndexOf(" in (", StringComparison.OrdinalIgnoreCase)].Trim();
            var candidates = expression[(open + 1)..close]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return (true, fields =>
                fields.TryGetValue(field, out var current) && candidates.Contains(current, StringComparer.Ordinal));
        }

        if (expression.Contains("!=", StringComparison.Ordinal))
        {
            var index = expression.IndexOf("!=", StringComparison.Ordinal);
            var field = expression[..index].Trim();
            var expected = expression[(index + 2)..].Trim();
            return (true, fields =>
                !fields.TryGetValue(field, out var current) ||
                !string.Equals(current, expected, StringComparison.Ordinal));
        }

        if (expression.Contains('='))
        {
            var index = expression.IndexOf('=');
            var field = expression[..index].Trim();
            var expected = expression[(index + 1)..].Trim();
            return (true, fields =>
                fields.TryGetValue(field, out var current) &&
                string.Equals(current, expected, StringComparison.Ordinal));
        }

        // 未识别表达式：不静默放行（门不合格 ⇒ ⊥ 语义），识别标记供上层可见化
        return Unrecognized;
    }

    private static (bool Recognized, Func<IReadOnlyDictionary<string, string>, bool> Evaluate) Unrecognized =>
        (false, _ => false);
}

/// <summary>
/// 选择集（声明装载；缺省含 <c>all</c>）。未声明 selectionId / 未识别表达式一律 <b>可见化诊断</b>
/// （每 id 只报一次），同时保持"不过滤通过"的保守语义（原实现静默丢弃读侧数据）。
/// </summary>
public sealed class SelectionSet
{
    private readonly Dictionary<string, ISelectionPredicate> _selections = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly INodeDiagnosticsSink _diagnostics;

    public SelectionSet(INodeDiagnosticsSink? diagnostics = null)
    {
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
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

    public bool Accept(string selectionId, IReadOnlyDictionary<string, string> fields)
    {
        if (!TryGet(selectionId, out var predicate))
        {
            ReportOnce($"selection.undeclared|{selectionId}", "selection.undeclared", selectionId,
                "选择未声明：该批数据被过滤（缺口可见化，不静默）");
            return false;
        }

        if (predicate is ExpressionSelectionPredicate expression && !expression.IsRecognized)
        {
            ReportOnce($"selection.unparsed|{selectionId}", "selection.unparsed", selectionId,
                $"选择表达式不可识别：判据不合格（⊥），不放行（表达式 '{expression.Expression}'）");
            return false;
        }

        return predicate.Accept(fields);
    }

    private void ReportOnce(string dedupKey, string code, string subject, string message)
    {
        lock (_reported)
        {
            if (!_reported.Add(dedupKey))
            {
                return;
            }
        }

        _diagnostics.Report(new NodeDiagnostic(code, NodeDiagnosticSeverity.Warning, message, subject));
    }
}
