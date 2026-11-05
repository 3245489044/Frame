using System.Text;
using Modules.Node.Spec;

namespace Modules.Node.Projection;

/// <summary>预算内渲染结果（文本 + 截断/丢弃清单 + 已用 token）。</summary>
/// <param name="Text">渲染文本（只读产物，不落库）。</param>
/// <param name="Truncated">被截断或丢弃条目（原因 <c>budget</c>）。</param>
/// <param name="UsedTokens">已用 token。</param>
public sealed record BudgetedRenderResult(
    string Text,
    IReadOnlyList<NodeSelectionDrop> Truncated,
    int UsedTokens);

/// <summary>
/// 预算内渲染（N7/R6）：只读渲染，<b>裁剪结果不持久化</b>；截断/丢弃一律进清单（可解释）。
/// <para>
/// 未声明 <c>budget.charsPerToken</c> ⇒ 超预算条目整条丢弃（不做字符级截断，避免静默使用缺省比例）。
/// </para>
/// </summary>
public sealed class BudgetedRender
{
    private readonly ITokenEstimator _estimator;
    private readonly string _truncationMarker;
    private readonly Func<NodeProjectionItem, string> _blockOf;

    /// <param name="estimator">token 估算端口（必填：渲染器不内置比例）。</param>
    /// <param name="truncationMarker">截断标记。</param>
    /// <param name="blockOf">条目 → 文本块（可空 = 载荷 JSON 或字段袋拼接）。</param>
    public BudgetedRender(
        ITokenEstimator estimator,
        string truncationMarker = "…[truncated]",
        Func<NodeProjectionItem, string>? blockOf = null)
    {
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _truncationMarker = truncationMarker;
        _blockOf = blockOf ?? DefaultBlockOf;
    }

    /// <summary>按预算渲染（<paramref name="budget"/> 为空/无 token 上限 ⇒ 全量渲染）。</summary>
    public BudgetedRenderResult Render(IReadOnlyList<NodeProjectionItem> items, NodeBudgetSpec? budget)
    {
        ArgumentNullException.ThrowIfNull(items);

        var tokenLimit = budget?.Tokens;
        var remaining = tokenLimit is > 0 ? tokenLimit.Value : int.MaxValue;
        var used = 0;
        var truncated = new List<NodeSelectionDrop>();
        var builder = new StringBuilder();

        foreach (var item in items)
        {
            var block = _blockOf(item);
            var tokens = _estimator.Estimate(block);

            if (tokens <= remaining)
            {
                builder.Append(block);
                remaining -= tokens;
                used += tokens;
                continue;
            }

            if (budget?.CharsPerToken is { } charsPerToken && charsPerToken > 0)
            {
                var allowedChars = Math.Max(0, remaining * charsPerToken);
                if (allowedChars > _truncationMarker.Length)
                {
                    var keep = Math.Min(block.Length, allowedChars - _truncationMarker.Length);
                    var cut = block[..keep] + _truncationMarker;
                    var cutTokens = _estimator.Estimate(cut);
                    if (cutTokens <= remaining)
                    {
                        builder.Append(cut);
                        remaining -= cutTokens;
                        used += cutTokens;
                        truncated.Add(new NodeSelectionDrop(item.Key, "budget", cutTokens));
                        continue;
                    }
                }
            }

            truncated.Add(new NodeSelectionDrop(item.Key, "budget", tokens));
        }

        return new BudgetedRenderResult(builder.ToString(), truncated, used);
    }

    private static string DefaultBlockOf(NodeProjectionItem item)
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
