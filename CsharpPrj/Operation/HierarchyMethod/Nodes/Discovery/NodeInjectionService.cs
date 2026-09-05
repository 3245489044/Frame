using System.Text;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Discovery;

/// <summary>
/// 选择层（selection，预算内注入）：给定 select 节点键集，从节点库加载载荷、逐节点压缩、按 token_budget 限额注入。
/// 框架侧强制预算（默认 6000 tokens）；超出预算的节点按序 dropped。实现「给得更少但更对」。
/// </summary>
public sealed class NodeInjectionService
{
    private readonly INodeStore _nodes;
    private readonly int _defaultBudgetTokens;

    public NodeInjectionService(INodeStore nodes, int defaultBudgetTokens = 6000)
    {
        _nodes = nodes;
        _defaultBudgetTokens = defaultBudgetTokens > 0 ? defaultBudgetTokens : 6000;
    }

    public async Task<InjectionResult> InjectAsync(string workflowId, IReadOnlyList<string> select, int? budgetTokens, CancellationToken ct)
    {
        var budget = budgetTokens is > 0 ? budgetTokens.Value : _defaultBudgetTokens;
        var result = new InjectionResult { BudgetTokens = budget };
        var remaining = budget;
        var sb = new StringBuilder();

        foreach (var key in select)
        {
            var node = await NodeRecordResolver.ResolveAsync(_nodes, workflowId, key, ct).ConfigureAwait(false);
            if (node is null) { result.Dropped.Add($"{key}(missing)"); continue; }

            var header = $"### {key} [{node.NodeType}/{node.Status}] {node.UpdatedAt}\n";
            var payload = node.PayloadJson ?? "{}";
            var headerTokens = TokenEstimator.Estimate(header.Length);

            if (headerTokens >= remaining)
            {
                result.Dropped.Add(key);
                continue;
            }

            // 剩余字符配额：剩余 token * 4（扣除 header 与末尾换行；截断标记另计）。
            const string marker = "…[truncated]";
            var allowedChars = remaining * 4 - header.Length - 1;
            var truncated = payload.Length > allowedChars;
            if (truncated)
                payload = payload[..Math.Max(0, allowedChars - marker.Length)] + marker;

            var block = header + payload + "\n";
            var blockTokens = TokenEstimator.Estimate(block.Length);
            if (blockTokens > remaining) { result.Dropped.Add(key); continue; } // 防御：估算边界

            sb.Append(block);
            remaining -= blockTokens;
            result.UsedTokens += blockTokens;
            result.Loaded.Add(key);
        }

        result.Injected = sb.ToString();
        return result;
    }
}
