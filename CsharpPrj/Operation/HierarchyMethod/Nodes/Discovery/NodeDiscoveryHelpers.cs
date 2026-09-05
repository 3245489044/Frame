using Operation.HierarchyMethod.Nodes;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Discovery;

/// <summary>节点记录解析：给定 workflow + 节点键，按「输出记录 → 业务节点记录」顺序解析（两种键形）。
/// 图端点 Id = {wf}.{node}（NodeRelationFactory.NodeId），GetTaskNodeKeysAsync 剥离 {wf}. 前缀返回裸 nodeKey；
/// 输出记录 NodeKey = nodes.{wf}.{node}（NodeOutputKeys），业务节点记录 NodeKey = 裸 {node}。</summary>
public static class NodeRecordResolver
{
    public static async Task<NodeRecord?> ResolveAsync(INodeStore nodes, string workflowId, string nodeKey, CancellationToken ct)
    {
        // 输出并入业务节点后：业务节点唯一记录（NodeKey=裸 {node}），直接读业务记录即可，
        // 不再查询 nodes.{wf}.{node} 输出记录（消除输出节点双写逻辑）。
        _ = workflowId;
        return await nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
    }
}

/// <summary>一句话摘要：NodeOutputDoc 端口取前 3 个（key=截断值），非文档载荷取首 120 字符。</summary>
public static class NodeSummaryBuilder
{
    private const int MaxPorts = 3;
    private const int MaxPortValueChars = 40;
    private const int MaxFallbackChars = 120;

    public static string Build(NodeRecord? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.PayloadJson)) return string.Empty;
        // 业务节点唯一载荷（BusinessNodePayload）：从 Outputs 端口取摘要。
        var payload = NodeOutputCodec.DecodeBusiness(node.PayloadJson);
        var doc = payload.Outputs;
        if (doc is null || doc.Ports is not { Count: > 0 })
        {
            var t = node.PayloadJson.Trim();
            return t.Length <= MaxFallbackChars ? t : t[..MaxFallbackChars] + "…";
        }
        var parts = new List<string>();
        foreach (var (k, v) in doc.Ports.Take(MaxPorts))
        {
            var text = v.Value?.ToString() ?? "null";
            text = text.Length <= MaxPortValueChars ? text : text[..MaxPortValueChars] + "…";
            parts.Add($"{k}={text}");
        }
        return string.Join(" | ", parts);
    }
}

/// <summary>token 估算（字符近似，4 字符 ≈ 1 token；对齐配置 compressor=tiktoken-truncate 的朴素实现）。</summary>
public static class TokenEstimator
{
    public static int Estimate(int charCount) => (int)Math.Ceiling(Math.Max(0, charCount) / 4.0);
    public static int Estimate(string? text) => Estimate(text?.Length ?? 0);
}
