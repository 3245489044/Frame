namespace Operation.DataStoreDriven.Relations;

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

        await EnsureProvisionedAsync(plan, request, ct).ConfigureAwait(false);

        return intent == RelationIntent.Remove
            ? await _commands.RemoveAsync(operation, request, ct).ConfigureAwait(false)
            : await _commands.EnsureAsync(operation, request, ct).ConfigureAwait(false);
    }

    /// <summary>端点 provision（谁建实体谁建镜像）：未注册 ⇒ 跳过并记诊断（不静默）。</summary>
    private async Task EnsureProvisionedAsync(RelationOperation plan, RelationRequest request, CancellationToken ct)
    {
        foreach (var (entityType, key) in new[]
                 {
                     (plan.Source, request.SourceKey),
                     (plan.Target, request.Targets.Count > 0 ? request.Targets[0] : null),
                 })
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var provision = _adapters.TryGet(entityType);
            if (provision is null)
            {
                _diagnostics?.RelationWriteSkipped(plan.Id, plan.Edge, "provision-unregistered", entityType, key);
                continue;
            }

            await provision(key, ct).ConfigureAwait(false);
        }
    }

    private string? ResolveOperation(RelationRequest request, RelationIntent intent)
    {
        if (!string.IsNullOrWhiteSpace(request.Operation)) return request.Operation;
        return null;
    }
}
