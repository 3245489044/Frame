using Operation.HierarchyMethod.Nodes;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.HierarchyMethod.Nodes.Discovery;

/// <summary>节点记录解析：由节点键解析业务节点记录 —— 输出并入业务节点后，裸 <c>{nodeKey}</c> 为唯一主键。
/// 图端点 Id = {wf}.{node}（NodeRelationFactory.NodeId，GetTaskNodeKeysAsync 剥离前缀返回裸键）；
/// 遗留 <c>nodes.{wf}.{node}</c> 输出记录不再查询（消除输出节点双写）。</summary>
public static class NodeRecordResolver
{
    /// <param name="nodes">节点库端口。</param>
    /// <param name="workflowId">调用方键形态对齐参数（输出并入业务节点后不参与键构造；裸键不含 workflowId 命名空间）。</param>
    /// <param name="nodeKey">节点键（裸 nodeKey）。</param>
    /// <param name="ct">取消令牌。</param>
    public static Task<NodeRecord?> ResolveAsync(INodeStore nodes, string? workflowId, string nodeKey, CancellationToken ct)
        => nodes.GetAsync(nodeKey, ct);
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
