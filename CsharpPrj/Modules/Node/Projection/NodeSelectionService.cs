using Modules.Node.Diagnostics;
using Modules.Node.Spec;

namespace Modules.Node.Projection;

/// <summary>被丢弃条目（原因词单源；<see cref="Estimate"/> 仅预算维度给出）。</summary>
/// <param name="Key">节点键。</param>
/// <param name="Reason">原因（<c>signal-missing</c>/<c>filter</c>/<c>limit</c>/<c>budget</c>）。</param>
/// <param name="Estimate">估算 token（可空）。</param>
public sealed record NodeSelectionDrop(string Key, string Reason, int? Estimate = null);

/// <summary>选择结果（有序条目 + 丢弃清单 + 已用预算）。</summary>
/// <param name="Items">入选条目（序 = 信号排序序）。</param>
/// <param name="Dropped">丢弃清单（逐条原因可解释）。</param>
/// <param name="UsedTokens">已用 token（未装配估算器 ⇒ 0）。</param>
public sealed record NodeSelectionResult(
    IReadOnlyList<NodeProjectionItem> Items,
    IReadOnlyList<NodeSelectionDrop> Dropped,
    int UsedTokens);

/// <summary>
/// 读侧选择唯一编排（N7）：<c>读投影 → 信号过滤 → 信号排名 → 条数上限 → token 预算 → dropped</c>。
/// <para>
/// 纪律：只读、不落库；预算截断只影响渲染，结果不持久化（R6）；未装配估算器 ⇒ 只做条数上限 + 诊断。
/// </para>
/// </summary>
public sealed class NodeSelectionService
{
    private readonly NodeReadProjection _projection;
    private readonly NodeSelectionSpec _defaultSpec;
    private readonly ITokenEstimator? _estimator;
    private readonly INodeDiagnosticsSink _diagnostics;

    /// <param name="projection">读投影（唯一读面）。</param>
    /// <param name="spec">框架声明中的选择/预算（<see cref="ReadScope.Selection"/> 优先）。</param>
    /// <param name="estimator">token 估算端口（可空 ⇒ 不静默假设比例）。</param>
    /// <param name="diagnostics">诊断出口。</param>
    public NodeSelectionService(
        NodeReadProjection projection,
        NodeSelectionSpec? spec = null,
        ITokenEstimator? estimator = null,
        INodeDiagnosticsSink? diagnostics = null)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _defaultSpec = spec ?? NodeSelectionSpec.Empty;
        _estimator = estimator;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    /// <summary>
    /// 按作用域选择（<see cref="ReadScope.Selection"/> 优先于构造期声明）。
    /// <paramref name="overlays"/> = 运行态/派生叠加源（可为信号/标签/地址载体）。
    /// </summary>
    public async Task<NodeSelectionResult> SelectAsync(
        ReadScope scope,
        IReadOnlyList<NodeProjectionItem>? overlays = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var spec = scope.Selection ?? _defaultSpec;
        var dropped = new List<NodeSelectionDrop>();

        var source = await _projection
            .ProjectAsync(scope with { Selection = null }, overlays, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<NodeProjectionItem>(source.Count);
        foreach (var item in source)
        {
            if (spec.SignalFilter is { } filter)
            {
                if (item.Signal is null)
                {
                    dropped.Add(new NodeSelectionDrop(item.Key, "signal-missing"));
                    continue;
                }

                if (!Accept(item.Signal, filter))
                {
                    dropped.Add(new NodeSelectionDrop(item.Key, "filter"));
                    continue;
                }
            }

            items.Add(item);
        }

        var ordered = Order(items, spec.SignalOrder);

        var limit = spec.Budget?.Limit;
        if (limit is > 0 && ordered.Count > limit.Value)
        {
            foreach (var item in ordered.Skip(limit.Value))
            {
                dropped.Add(new NodeSelectionDrop(item.Key, "limit"));
            }

            ordered = ordered.Take(limit.Value).ToList();
        }

        var usedTokens = 0;
        var tokenBudget = spec.Budget?.Tokens;
        if (tokenBudget is > 0)
        {
            if (_estimator is null)
            {
                _diagnostics.Report(new NodeDiagnostic(
                    "budget.estimator-missing",
                    NodeDiagnosticSeverity.Warning,
                    "声明了 token 预算但未装配估算器：仅执行条数上限（不静默采用缺省比例）",
                    scope.Domain ?? scope.KeyPrefix ?? string.Empty));
            }
            else
            {
                var kept = new List<NodeProjectionItem>(ordered.Count);
                foreach (var item in ordered)
                {
                    var estimate = _estimator.Estimate(EstimateTextOf(item));
                    if (usedTokens + estimate > tokenBudget.Value)
                    {
                        dropped.Add(new NodeSelectionDrop(item.Key, "budget", estimate));
                        continue;
                    }

                    usedTokens += estimate;
                    kept.Add(item);
                }

                ordered = kept;
            }
        }
        else if (_estimator is not null)
        {
            usedTokens = ordered.Sum(item => _estimator.Estimate(EstimateTextOf(item)));
        }

        return new NodeSelectionResult(ordered, dropped, usedTokens);
    }

    private static IReadOnlyList<NodeProjectionItem> Order(
        IReadOnlyList<NodeProjectionItem> items,
        IReadOnlyList<SignalOrderKey>? order)
    {
        if (order is not { Count: > 0 })
        {
            return items;
        }

        var list = items.ToList();
        list.Sort((left, right) => Compare(left, right, order));
        return list;
    }

    private static int Compare(NodeProjectionItem left, NodeProjectionItem right, IReadOnlyList<SignalOrderKey> order)
    {
        foreach (var key in order)
        {
            var leftValue = left.Signal?.Get(key.Field);
            var rightValue = right.Signal?.Get(key.Field);

            // 缺字段排末位（不伪造 0）
            if (leftValue is null && rightValue is null)
            {
                continue;
            }

            if (leftValue is null)
            {
                return 1;
            }

            if (rightValue is null)
            {
                return -1;
            }

            var result = double.TryParse(leftValue, out var leftNumber) &&
                         double.TryParse(rightValue, out var rightNumber)
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(leftValue, rightValue, StringComparison.Ordinal);

            if (result == 0)
            {
                continue;
            }

            return key.Descending ? -result : result;
        }

        return string.Compare(left.Key, right.Key, StringComparison.Ordinal);
    }

    /// <summary>信号过滤（合取）：期望值给定时须相等；未给定时只要求字段存在且非空。</summary>
    private static bool Accept(NodeSignal signal, SignalFilter filter)
    {
        foreach (var field in filter.Fields)
        {
            var actual = signal.Get(field);
            if (actual is null)
            {
                return false;
            }

            if (filter.Values is not null &&
                filter.Values.TryGetValue(field, out var expected) &&
                !string.Equals(actual, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string EstimateTextOf(NodeProjectionItem item)
    {
        if (!string.IsNullOrEmpty(item.PayloadJson))
        {
            return item.PayloadJson;
        }

        return string.Join(
            ";",
            item.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));
    }
}
