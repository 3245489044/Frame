namespace Modules.Node.Read;

/// <summary>
/// 键集来源端口（边 / 容器 / 前缀 / 全量）——**唯一**由上层实现（框架零存储依赖）。
/// <para>
/// 批量接口用于消 N+1：实现须一次枚举后按锚点分组；默认实现退化为逐条（可用于测试/小规模）。
/// </para>
/// </summary>
public interface INodeKeySetSource
{
    /// <summary>解析单个锚点的键集（原始键；归一由 <see cref="NodeKeySetResolver"/> 承担）。</summary>
    Task<NodeKeySetResult> ResolveAsync(NodeKeySetRequest request, CancellationToken ct = default);

    /// <summary>批量解析（一次枚举 + 按锚点分组）；返回 锚点键 → 结果。</summary>
    async Task<IReadOnlyDictionary<string, NodeKeySetResult>> ResolveManyAsync(
        NodeKeySetScope scope,
        IReadOnlyList<string> keys,
        string? workflowId,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, NodeKeySetResult>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            result[key] = await ResolveAsync(new NodeKeySetRequest(scope, key, workflowId), ct)
                .ConfigureAwait(false);
        }

        return result;
    }
}
