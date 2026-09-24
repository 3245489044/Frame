using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Spec;

public sealed record NodeChainMergeAction(string Kind, string NodeKey, string? RenamedTo, string Reason);

public sealed record ChainComposeResult(
    string MergedJson,
    IReadOnlyList<NodeChainMergeAction> Actions,
    IReadOnlyList<NodeDiagnostic> Diagnostics)
{
    public int NodeCount => Actions.Count(a => !string.Equals(a.Kind, "identical", StringComparison.Ordinal));
}

/// <summary>
/// 链合并器（G8 收编）：多链合并 + 三态去重（相同 / 新增 / 冲突改名）+ 引用重写。
/// 只产合并后的 JSON 与动作清单，<b>不做位置分配</b>（位置由声明决定）。
/// </summary>
public sealed class NodeChainComposer
{
    private readonly NodeReferenceIndex? _references;
    private readonly INodeDiagnosticsSink _diagnostics;

    public NodeChainComposer(NodeReferenceIndex? references = null, INodeDiagnosticsSink? diagnostics = null)
    {
        _references = references;
        _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;
    }

    public ChainComposeResult Compose(IReadOnlyList<SpecDocument> chains)
    {
        var diagnostics = new List<NodeDiagnostic>();
        var actions = new List<NodeChainMergeAction>();
        var merged = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var order = new List<string>();
        var chainIds = new List<string>();

        foreach (var document in chains)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(document.Json, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (JsonException ex)
            {
                diagnostics.Add(new NodeDiagnostic(
                    "chain.parse-error",
                    NodeDiagnosticSeverity.Error,
                    $"链声明解析失败: {ex.Message}",
                    document.Name));
                continue;
            }

            if (root is null)
            {
                continue;
            }

            var chainId = root["ChainId"]?.GetValue<string>() ?? root["Id"]?.GetValue<string>() ?? document.Name;
            chainIds.Add(chainId);

            if (root["Nodes"] is not JsonArray nodes)
            {
                diagnostics.Add(new NodeDiagnostic(
                    "chain.nodes-missing",
                    NodeDiagnosticSeverity.Error,
                    "链声明缺少 Nodes 数组",
                    chainId));
                continue;
            }

            foreach (var item in nodes)
            {
                if (item is not JsonObject node)
                {
                    diagnostics.Add(new NodeDiagnostic(
                        "chain.node-invalid",
                        NodeDiagnosticSeverity.Warning,
                        "链内存在非对象节点：已跳过（不静默）",
                        chainId));
                    continue;
                }

                var nodeKey = node["Callkey"]?.GetValue<string>() ?? node["Id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(nodeKey))
                {
                    diagnostics.Add(new NodeDiagnostic(
                        "chain.node-key-missing",
                        NodeDiagnosticSeverity.Error,
                        "节点缺少键（Callkey / Id）：已跳过",
                        chainId));
                    continue;
                }

                if (!merged.TryGetValue(nodeKey, out var existing))
                {
                    merged[nodeKey] = node.DeepClone();
                    order.Add(nodeKey);
                    actions.Add(new NodeChainMergeAction("added", nodeKey, null, chainId));
                    continue;
                }

                if (JsonNode.DeepEquals(existing, node))
                {
                    actions.Add(new NodeChainMergeAction("identical", nodeKey, null, chainId));
                    continue;
                }

                var renamed = UniqueKey(merged, nodeKey);
                var rewritten = _references is null
                    ? node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
                    : _references.RenameKey(nodeKey, renamed, node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })).Json;

                merged[renamed] = JsonNode.Parse(rewritten);
                order.Add(renamed);
                actions.Add(new NodeChainMergeAction("conflict-renamed", nodeKey, renamed, chainId));

                diagnostics.Add(new NodeDiagnostic(
                    "chain.key-conflict",
                    NodeDiagnosticSeverity.Warning,
                    $"节点键冲突：'{nodeKey}' 与已合并节点不同载荷，已改名为 '{renamed}' 并级联引用",
                    chainId));
            }
        }

        var mergedNodes = new JsonArray();
        foreach (var key in order)
        {
            mergedNodes.Add(merged[key]?.DeepClone());
        }

        var result = new JsonObject
        {
            ["ChainId"] = chainIds.Count == 1 ? chainIds[0] : "merged",
            ["MergedFrom"] = new JsonArray(chainIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
            ["Nodes"] = mergedNodes,
        };

        foreach (var diagnostic in diagnostics)
        {
            _diagnostics.Report(diagnostic);
        }

        return new ChainComposeResult(
            result.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            actions,
            diagnostics);
    }

    private static string UniqueKey(IReadOnlyDictionary<string, JsonNode> existing, string nodeKey)
    {
        var index = 1;
        while (true)
        {
            var candidate = $"{nodeKey}__{index}";
            if (!existing.ContainsKey(candidate))
            {
                return candidate;
            }

            index++;
        }
    }
}
