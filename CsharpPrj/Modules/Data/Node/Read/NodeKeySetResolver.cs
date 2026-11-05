using Modules.Node.Identity;
using Modules.Node.Projection;

namespace Modules.Node.Read;

/// <summary>
/// 键集解析**单源**：多源并集 → 端点归一 → 去重 → 键序 → Limit + 原因归类。
/// <para>
/// 归一唯一实现 = <see cref="NodeEndpointIds.TryParseNodeIdentity"/>（禁止调用点自写剥前缀）；
/// 序唯一实现 = <see cref="NodeKeyOrdinalComparer"/>（禁止各调用点 <c>OrderBy(string)</c>）。
/// </para>
/// </summary>
public sealed class NodeKeySetResolver
{
    /// <summary>原因采样上限（诊断可见，不刷屏）。</summary>
    public const int ReasonSampleLimit = 5;

    private readonly IReadOnlyList<INodeKeySetSource> _sources;
    private readonly string? _workflowId;

    /// <param name="sources">键集来源（声明序 = 优先级；任一可用即并集）。</param>
    /// <param name="workflowId">工作流命名空间（端点归一；null = 按端点面自解）。</param>
    public NodeKeySetResolver(IReadOnlyList<INodeKeySetSource> sources, string? workflowId = null)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _workflowId = string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
    }

    /// <summary>解析单锚点键集。</summary>
    public async Task<NodeKeySetResult> ResolveAsync(NodeKeySetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return NodeKeySetResult.Empty(NodeKeySetOrigin.Full, NodeKeySetReasons.EmptyKey);
        }

        if (_sources.Count == 0)
        {
            return NodeKeySetResult.Empty(NodeKeySetOrigin.Full, NodeKeySetReasons.SourceUnavailable);
        }

        var workflowId = string.IsNullOrWhiteSpace(request.WorkflowId) ? _workflowId : request.WorkflowId;
        var raw = new List<string>();
        var reasons = new List<string>();
        var origin = NodeKeySetOrigin.Full;
        var anyAvailable = false;

        foreach (var source in _sources)
        {
            var result = await source.ResolveAsync(request with { WorkflowId = workflowId }, ct)
                .ConfigureAwait(false);
            if (IsUnavailable(result))
            {
                continue;
            }

            anyAvailable = true;
            if (raw.Count == 0 || OriginRank(result.Origin) < OriginRank(origin))
            {
                origin = result.Origin;
            }

            raw.AddRange(result.Keys);
            reasons.AddRange(result.Reasons);
        }

        if (!anyAvailable)
        {
            return NodeKeySetResult.Empty(origin, NodeKeySetReasons.SourceUnavailable);
        }

        var normalized = Normalize(raw, workflowId, reasons);
        if (normalized.Count == 0 && reasons.Count == 0)
        {
            reasons.Add(NodeKeySetReasons.NoBinding);
        }

        var limited = request.Limit is { } limit && limit > 0 && normalized.Count > limit
            ? normalized.Take(limit).ToList()
            : normalized;

        return new NodeKeySetResult(limited, origin, DistinctReasons(reasons));
    }

    /// <summary>批量解析（委托各源批量接口；归一/去重/排序仍走单点）。</summary>
    public async Task<IReadOnlyDictionary<string, NodeKeySetResult>> ResolveManyAsync(
        NodeKeySetScope scope,
        IReadOnlyList<string> keys,
        string? workflowId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var wf = string.IsNullOrWhiteSpace(workflowId) ? _workflowId : workflowId;
        var result = new Dictionary<string, NodeKeySetResult>(StringComparer.Ordinal);
        if (_sources.Count == 0)
        {
            foreach (var key in keys)
            {
                result[key] = NodeKeySetResult.Empty(NodeKeySetOrigin.Full, NodeKeySetReasons.SourceUnavailable);
            }

            return result;
        }

        var perSource = new List<IReadOnlyDictionary<string, NodeKeySetResult>>(_sources.Count);
        foreach (var source in _sources)
        {
            perSource.Add(await source.ResolveManyAsync(scope, keys, wf, ct).ConfigureAwait(false));
        }

        foreach (var key in keys)
        {
            var raw = new List<string>();
            var reasons = new List<string>();
            var origin = NodeKeySetOrigin.Full;
            var anyAvailable = false;
            foreach (var map in perSource)
            {
                if (!map.TryGetValue(key, out var one) || IsUnavailable(one))
                {
                    continue;
                }

                anyAvailable = true;
                if (raw.Count == 0 || OriginRank(one.Origin) < OriginRank(origin)) origin = one.Origin;
                raw.AddRange(one.Keys);
                reasons.AddRange(one.Reasons);
            }

            if (!anyAvailable)
            {
                result[key] = NodeKeySetResult.Empty(origin, NodeKeySetReasons.SourceUnavailable);
                continue;
            }

            var normalized = Normalize(raw, wf, reasons);
            if (normalized.Count == 0 && reasons.Count == 0)
            {
                reasons.Add(NodeKeySetReasons.NoBinding);
            }

            result[key] = new NodeKeySetResult(normalized, origin, DistinctReasons(reasons));
        }

        return result;
    }

    private static List<string> Normalize(List<string> raw, string? workflowId, List<string> reasons)
    {
        var keys = new List<string>(raw.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                AddReason(reasons, NodeKeySetReasons.EmptyKey, string.Empty);
                continue;
            }

            if (NodeEndpointIds.TryParseNodeIdentity(item, workflowId, out var key))
            {
                if (seen.Add(key)) keys.Add(key);
                continue;
            }

            AddReason(
                reasons,
                NodeEndpointIds.IsForeignFamily(item.Trim())
                    ? NodeKeySetReasons.ForeignNamespace
                    : NodeKeySetReasons.UnparsableKey,
                item);
        }

        keys.Sort(NodeKeyOrdinalComparer.Instance);
        return keys;
    }

    private static void AddReason(List<string> reasons, string reason, string sample)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }

        if (sample.Length > 0 &&
            reasons.Count(r => r.StartsWith(reason + ":", StringComparison.Ordinal)) < ReasonSampleLimit)
        {
            reasons.Add($"{reason}:{sample}");
        }
    }

    private static bool IsUnavailable(NodeKeySetResult result)
        => result.Reasons.Contains(NodeKeySetReasons.SourceUnavailable, StringComparer.Ordinal);

    private static int OriginRank(NodeKeySetOrigin origin) => origin switch
    {
        NodeKeySetOrigin.Edge => 0,
        NodeKeySetOrigin.Container => 1,
        NodeKeySetOrigin.KeyPrefix => 2,
        _ => 3,
    };

    private static IReadOnlyList<string> DistinctReasons(List<string> reasons)
    {
        var result = new List<string>(reasons.Count);
        foreach (var reason in reasons)
        {
            if (!result.Contains(reason, StringComparer.Ordinal)) result.Add(reason);
        }

        return result;
    }
}

