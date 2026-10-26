using Modules.Node.Projection;

namespace Modules.Node.Read;

/// <summary>域读请求（键集 + 读侧限定；运行态无关）。</summary>
/// <param name="DomainKey">读域。</param>
/// <param name="Keys">键集请求（层级锚点；null = 走显式键集/全量声明）。</param>
/// <param name="SelectionId">读侧 selection 名（可空 = 声明默认）。</param>
/// <param name="Limit">条数上限。</param>
/// <param name="Loc">位置坐标（可空；给出时经位置反解收窄）。</param>
/// <param name="PlacementIds">位置收窄（可空 = 本域可读位置）。</param>
/// <param name="Container">期望容器（FW-2 读限定；null = 不限）。</param>
/// <param name="Round">期望轮次（FW-2；null = 不限）。</param>
/// <param name="ExplicitKeys">显式键集（有界读；与 <paramref name="Keys"/> 二选一）。</param>
/// <param name="ExplicitFull">显式声明全量（<see cref="NodeKeySetScope.Full"/>；数据库页/诊断）。</param>
/// <param name="Overlays">运行态覆盖项（桶键 = Key|PlacementId）。</param>
public sealed record DomainReadRequest(
    string DomainKey,
    NodeKeySetRequest? Keys = null,
    string? SelectionId = null,
    int? Limit = null,
    string? Loc = null,
    string? KeyPrefix = null,
    IReadOnlyList<string>? PlacementIds = null,
    string? Container = null,
    int? Round = null,
    IReadOnlyList<string>? ExplicitKeys = null,
    bool ExplicitFull = false,
    IReadOnlyList<NodeProjectionItem>? Overlays = null);

/// <summary>
/// 节点读**单一门面**：键集（<see cref="NodeKeySetResolver"/>）→ 投影（<see cref="NodeReadProjection"/>）。
/// <para>
/// 投影按域解析（<c>Func&lt;domainKey, Projection?&gt;</c>）；未装配 ⇒ 可用但空 + 原因（不静默）。
/// 缺行由本门面计算（<c>Keys ∖ Items.Key</c>），不要求投影器改签名。
/// </para>
/// </summary>
public sealed class NodeReadService
{
    private readonly NodeKeySetResolver? _keys;
    private readonly Func<string, NodeReadProjection?> _projection;
    private readonly NodeReadGuard? _guard;
    private readonly string? _workflowId;

    /// <param name="keys">键集解析单源（显式键集读可为 null）。</param>
    /// <param name="projection">按域取投影器（未装配 ⇒ null）。</param>
    /// <param name="frameworks">按域取域框架（限定判定的位置模板来源；null = 不做键族/身份判定）。</param>
    /// <param name="workflowId">工作流命名空间（端点归一；可空）。</param>
    public NodeReadService(
        NodeKeySetResolver? keys,
        Func<string, NodeReadProjection?> projection,
        Func<string, NodeFramework?>? frameworks = null,
        string? workflowId = null)
    {
        _keys = keys;
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _guard = frameworks is null ? null : new NodeReadGuard(frameworks);
        _workflowId = string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
    }

    /// <summary>读：键集 → 限定 → 投影 → 缺行回填 → 三态（空必带原因）。</summary>
    public async Task<NodeReadResult> ReadAsync(DomainReadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        NodeKeySetResult keySet;
        if (request.ExplicitKeys is { } explicitKeys)
        {
            keySet = new NodeKeySetResult(explicitKeys, NodeKeySetOrigin.Full, Array.Empty<string>());
        }
        else if (request.Keys is { } spec && _keys is not null)
        {
            var scope = spec with
            {
                WorkflowId = string.IsNullOrWhiteSpace(spec.WorkflowId) ? _workflowId : spec.WorkflowId,
                Limit = spec.Limit ?? request.Limit,
            };
            keySet = await _keys.ResolveAsync(scope, ct).ConfigureAwait(false);
        }
        else
        {
            keySet = NodeKeySetResult.Empty(NodeKeySetOrigin.Full);
        }

        // 限定必达（I2）：Scoped 且无键、无容器、未显式声明全量 ⇒ 不可用（禁静默读全域）。
        var hasScopeCoordinate =
            keySet.Keys.Count > 0 ||
            !string.IsNullOrWhiteSpace(request.Container) ||
            !string.IsNullOrWhiteSpace(request.KeyPrefix) ||
            request.ExplicitFull ||
            request.ExplicitKeys is { Count: > 0 };
        if (!hasScopeCoordinate)
        {
            return new NodeReadResult(
                request.DomainKey,
                keySet,
                Array.Empty<NodeProjectionItem>(),
                new[] { "scope-undefined" },
                NodeReadStatus.Unavailable,
                new[] { NodeReadReason.ScopeUndefined });
        }

        if (keySet.Keys.Count == 0 && request.ExplicitKeys is null)
        {
            // 键集合法空（no-binding 等）：可用但空，不清面。
            return new NodeReadResult(
                request.DomainKey,
                keySet,
                Array.Empty<NodeProjectionItem>(),
                keySet.Reasons,
                NodeReadStatus.OkButEmpty,
                MapCodes(keySet.Reasons));
        }

        var projection = _projection(request.DomainKey);
        if (projection is null)
        {
            var unavailableReasons = keySet.Reasons.Concat(new[] { NodeKeySetReasons.SourceUnavailable }).ToArray();
            return new NodeReadResult(
                request.DomainKey,
                keySet,
                Array.Empty<NodeProjectionItem>(),
                unavailableReasons,
                NodeReadStatus.Unavailable,
                MapCodes(unavailableReasons));
        }

        var projected = await projection.ProjectAsync(
                new ReadScope(
                    Domain: request.DomainKey,
                    PlacementIds: request.PlacementIds,
                    SelectionId: request.SelectionId,
                    KeyPrefix: request.KeyPrefix,
                    Loc: request.Loc,
                    Limit: request.Limit,
                    Container: request.Container,
                    Round: request.Round,
                    Keys: keySet.Keys.Count > 0 ? keySet.Keys : null),
                request.Overlays,
                ct)
            .ConfigureAwait(false);

        // 限定（唯一判定点）：容器/轮次裁剪 + 键族隔离。
        var items = projected;
        var reasons = new List<string>(keySet.Reasons);
        var codes = new List<NodeReadReason>(MapCodes(keySet.Reasons));
        if (_guard is not null)
        {
            var guarded = _guard.Admit(
                new ReadGuardScope(
                    request.DomainKey,
                    request.Container,
                    request.Round,
                    keySet.Keys.Count,
                    request.ExplicitFull),
                projected);
            items = guarded.Items;
            reasons.AddRange(guarded.Samples);
            foreach (var code in guarded.ReasonCodes)
            {
                if (!codes.Contains(code))
                {
                    codes.Add(code);
                }
            }
        }

        // 显式键集读的缺行可见化（Keys ∖ 实际行）：不伪造行，不静默吞。
        var requested = keySet.Keys;
        if (requested.Count > 0)
        {
            var found = new HashSet<string>(items.Select(item => item.Key), StringComparer.Ordinal);
            var missing = requested.Where(key => !found.Contains(key)).ToArray();
            if (missing.Length > 0)
            {
                if (!reasons.Contains(NodeKeySetReasons.KeyMissing, StringComparer.Ordinal))
                {
                    reasons.Add(NodeKeySetReasons.KeyMissing);
                }

                if (!codes.Contains(NodeReadReason.KeyMissing))
                {
                    codes.Add(NodeReadReason.KeyMissing);
                }

                foreach (var key in missing.Take(NodeKeySetResolver.ReasonSampleLimit))
                {
                    reasons.Add($"{NodeKeySetReasons.KeyMissing}:{key}");
                }
            }
        }

        var status = items.Count > 0 ? NodeReadStatus.Ok : NodeReadStatus.OkButEmpty;
        return new NodeReadResult(request.DomainKey, keySet, items, reasons, status, codes);
    }

    /// <summary>
    /// 原始投影入口（无选择式；供 selection 评分复用）：**唯一** <c>ProjectAsync</c> 调用点在本类内（I1）。
    /// </summary>
    public static Task<IReadOnlyList<NodeProjectionItem>> ProjectRawAsync(
        NodeReadProjection projection,
        ReadScope scope,
        IReadOnlyList<NodeProjectionItem>? overlays = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection.ProjectAsync(scope, overlays, ct);
    }

    private static IReadOnlyList<NodeReadReason> MapCodes(IEnumerable<string> reasons)
    {
        NodeReadReasons.TryMap(reasons, out var codes);
        return codes;
    }
}
