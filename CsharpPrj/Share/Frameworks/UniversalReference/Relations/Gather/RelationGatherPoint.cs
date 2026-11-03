namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 关系聚拢点实现：把「operation 解析 / 端点 provision / 写读转发」收到单一求值入口。
/// <para>调用方不持 operation 字面量、不直连 <c>IResourceReferenceStore</c>；域差异全部经
/// <see cref="RelationAdapterRegistry"/> 数据化。</para>
/// </summary>
public sealed class RelationGatherPoint : IRelationGatherPoint, IRelationGatherPointByEdge
{
    private readonly IRelationCommandPort _commands;
    private readonly IRelationQueryPort _queries;
    private readonly RelationPlanCatalog _catalog;
    private readonly RelationAdapterRegistry _adapters;
    private readonly IRelationDiagnostics? _diagnostics;

    public RelationGatherPoint(
        IRelationCommandPort commands,
        IRelationQueryPort queries,
        RelationPlanCatalog catalog,
        RelationAdapterRegistry adapters,
        IRelationDiagnostics? diagnostics = null)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _diagnostics = diagnostics;
    }

    public RelationOperation? Describe(string operation) => _catalog.Resolve(operation);

    public async Task<RelationResult> InvokeAsync(
        RelationIntent intent, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operation = ResolveOperation(request, intent);
        if (operation is null)
            return RelationResult.Rejected(string.Empty, null,
                $"未解析到唯一 operation（edge+intent={intent}；须在 [Relation] 声明）");

        return await InvokeCoreAsync(intent, operation, request, ct).ConfigureAwait(false);
    }

    public async Task<RelationResult> InvokeByEdgeAsync(
        string edge, RelationIntent intent, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edge);
        ArgumentNullException.ThrowIfNull(request);

        var operation = _catalog.ResolveByEdge(edge, remove: intent == RelationIntent.Remove);
        if (operation is null)
            return RelationResult.Rejected(edge, edge, $"edge '{edge}' 未声明唯一 {(intent == RelationIntent.Remove ? "remove" : "ensure")} operation");

        return await InvokeCoreAsync(intent, operation.Id, request, ct).ConfigureAwait(false);
    }

    public Task<RelationResult> InvokeByOperationAsync(
        string operation, RelationIntent intent, RelationRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(request);
        return InvokeCoreAsync(intent, operation, request, ct);
    }

    public Task<RelationQueryResult> DeriveAsync(string deriveId, RelationScope scope, CancellationToken ct = default)
        => _queries.DeriveAsync(deriveId, scope, ct);

    private async Task<RelationResult> InvokeCoreAsync(
        RelationIntent intent, string operation, RelationRequest request, CancellationToken ct)
    {
        var plan = _catalog.Resolve(operation);
        if (plan is null)
            return RelationResult.Rejected(operation, null, $"operation '{operation}' 未在 [Relation] 声明");

        // provision 只在写入方向发生（删除方向补建端点会复活镜像，语义相反）。
        if (intent == RelationIntent.Ensure)
            await EnsureProvisionedAsync(plan, request, ct).ConfigureAwait(false);

        return intent == RelationIntent.Remove
            ? await _commands.RemoveAsync(operation, request, ct).ConfigureAwait(false)
            : await _commands.EnsureAsync(operation, request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 端点 provision（谁建实体谁建镜像）：声明面 <c>provision=</c> 给定实体集（缺省 = src+tgt）；
    /// 未注册 ⇒ 跳过并记诊断（不静默）。同一 (实体,键) 只镜像一次。
    /// </summary>
    private async Task EnsureProvisionedAsync(RelationOperation plan, RelationRequest request, CancellationToken ct)
    {
        var entities = string.IsNullOrWhiteSpace(plan.Provision)
            ? new[] { plan.Source, plan.Target }
            : plan.Provision.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var provision = _adapters.TryGet(entity);
            if (provision is null)
            {
                _diagnostics?.RelationWriteUnavailable(
                    plan.Id, plan.Edge, "provision-unregistered", entity,
                    string.Equals(entity, plan.Target, StringComparison.Ordinal) && request.Targets.Count > 0
                        ? request.Targets[0]
                        : request.SourceKey);
                continue;
            }

            foreach (var key in KeysOf(entity, plan, request))
            {
                if (!seen.Add(entity + "|" + key)) continue;
                await provision(key, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>该端点实体在本请求中对应的键集（批量 arity=many 逐键 provision）。</summary>
    private static IEnumerable<string> KeysOf(string entity, RelationOperation plan, RelationRequest request)
    {
        if (string.Equals(entity, plan.Source, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(request.SourceKey)) yield return request.SourceKey;
            yield break;
        }

        if (!string.Equals(entity, plan.Target, StringComparison.Ordinal)) yield break;

        foreach (var key in request.Targets)
        {
            if (!string.IsNullOrWhiteSpace(key)) yield return key;
        }
    }

    private string? ResolveOperation(RelationRequest request, RelationIntent intent)
    {
        if (!string.IsNullOrWhiteSpace(request.Operation)) return request.Operation;
        return null;
    }
}
