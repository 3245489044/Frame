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
            if (!NodeJson.TryParse(document.Json, out var root, out var parseError))
            {
                diagnostics.Add(new NodeDiagnostic(
                    "chain.parse-error",
                    NodeDiagnosticSeverity.Error,
                    $"链声明解析失败: {parseError ?? "空文档"}",
                    document.Name));
                continue;
            }

            if (root is null)
            {
                continue;
            }

            var chainId = ReadString(root["ChainId"]) ?? ReadString(root["Id"]) ?? document.Name;
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

                var nodeKey = ReadString(node["Callkey"]) ?? ReadString(node["Id"]);
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
                var payload = node.ToJsonString(NodeJson.WriteOptions);
                var rewritten = _references is null ? payload : _references.RenameKey(nodeKey, renamed, payload).Json;

                if (!NodeJson.TryParse(rewritten, out var reparsed, out _) || reparsed is null)
                {
                    // 自产 JSON 不可解析：不可能路径，仍可见化（不静默丢节点）
                    diagnostics.Add(new NodeDiagnostic(
                        "chain.rewrite-invalid",
                        NodeDiagnosticSeverity.Error,
                        "改名后节点 JSON 不可解析：该节点未合并",
                        nodeKey));
                    continue;
                }

                merged[renamed] = reparsed;
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
            result.ToJsonString(NodeJson.WriteOptions),
            actions,
            diagnostics);
    }

    private static string? ReadString(JsonNode? node) =>
        NodeJson.TryReadString(node, out var text) ? text : null;

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
