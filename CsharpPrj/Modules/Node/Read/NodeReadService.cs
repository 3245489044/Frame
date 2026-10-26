using Modules.Node.Projection;

namespace Modules.Node.Read;

/// <summary>域读请求（键集 + 读侧裁剪；运行态无关）。</summary>
/// <param name="DomainKey">读域。</param>
/// <param name="Keys">键集请求（层级锚点）。</param>
/// <param name="SelectionId">读侧 selection 名（可空 = 声明默认）。</param>
/// <param name="Limit">条数上限。</param>
/// <param name="Loc">位置坐标（可空；给出时经位置反解收窄）。</param>
public sealed record DomainReadRequest(
    string DomainKey,
    NodeKeySetRequest Keys,
    string? SelectionId = null,
    int? Limit = null,
    string? Loc = null);

/// <summary>
/// 节点读**单一门面**：键集（<see cref="NodeKeySetResolver"/>）→ 投影（<see cref="NodeReadProjection"/>）。
/// <para>
/// 投影按域解析（<c>Func&lt;domainKey, Projection?&gt;</c>）；未装配 ⇒ 可用但空 + 原因（不静默）。
/// 缺行由本门面计算（<c>Keys ∖ Items.Key</c>），不要求投影器改签名。
/// </para>
/// </summary>
public sealed class NodeReadService
{
    private readonly NodeKeySetResolver _keys;
    private readonly Func<string, NodeReadProjection?> _projection;
    private readonly string? _workflowId;

    /// <param name="keys">键集解析单源。</param>
    /// <param name="projection">按域取投影器（未装配 ⇒ null）。</param>
    /// <param name="workflowId">工作流命名空间（端点归一；可空）。</param>
    public NodeReadService(
        NodeKeySetResolver keys,
        Func<string, NodeReadProjection?> projection,
        string? workflowId = null)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _workflowId = string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
    }

    /// <summary>读：键集 → 投影 → 原因（空必带原因）。</summary>
    public async Task<NodeReadResult> ReadAsync(DomainReadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = request.Keys with
        {
            WorkflowId = string.IsNullOrWhiteSpace(request.Keys.WorkflowId) ? _workflowId : request.Keys.WorkflowId,
            Limit = request.Keys.Limit ?? request.Limit,
        };
        var keySet = await _keys.ResolveAsync(scope, ct).ConfigureAwait(false);
        if (keySet.Keys.Count == 0)
        {
            return new NodeReadResult(request.DomainKey, keySet, Array.Empty<NodeProjectionItem>(), keySet.Reasons);
        }

        var projection = _projection(request.DomainKey);
        if (projection is null)
        {
            var unavailableReasons = keySet.Reasons.Concat(new[] { NodeKeySetReasons.SourceUnavailable }).ToArray();
            return new NodeReadResult(request.DomainKey, keySet, Array.Empty<NodeProjectionItem>(), unavailableReasons);
        }

        var items = await projection.ProjectAsync(
                new ReadScope(
                    Domain: request.DomainKey,
                    SelectionId: request.SelectionId,
                    Loc: request.Loc,
                    Limit: request.Limit,
                    Keys: keySet.Keys),
                cancellationToken: ct)
            .ConfigureAwait(false);

        var reasons = new List<string>(keySet.Reasons);
        var found = new HashSet<string>(items.Select(item => item.Key), StringComparer.Ordinal);
        var missing = keySet.Keys.Where(key => !found.Contains(key)).ToArray();
        if (missing.Length > 0)
        {
            reasons.Add(NodeKeySetReasons.KeyMissing);
            foreach (var key in missing.Take(NodeKeySetResolver.ReasonSampleLimit))
            {
                reasons.Add($"{NodeKeySetReasons.KeyMissing}:{key}");
            }
        }

        return new NodeReadResult(request.DomainKey, keySet, items, reasons);
    }
}
