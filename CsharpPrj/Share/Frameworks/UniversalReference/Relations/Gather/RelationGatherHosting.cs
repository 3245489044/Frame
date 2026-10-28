namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 关系聚拢点的单一装配点（仿 NodeGatherHosting）：逐 operation 校验声明可达性，
/// 单条缺失只可见化、不中断整体装配（故障域不放大）。
/// </summary>
public static class RelationGatherHosting
{
    /// <summary>装配并返回「已就绪 operation 清单 + 未就绪原因」。</summary>
    public static RelationGatherHostingResult Attach(
        RelationPlanCatalog catalog,
        IRelationCommandPort commands,
        IRelationQueryPort queries,
        RelationAdapterRegistry adapters,
        IRelationDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(adapters);

        var ready = new List<string>();
        var missing = new List<string>();

        if (catalog.IsEmpty)
        {
            diagnostics?.RelationPlanMissing("gather-hosting：计划为空，聚拢点将只返回 NotAssembled");
            return new RelationGatherHostingResult(new RelationGatherPoint(commands, queries, catalog, adapters, diagnostics),
                ready, new[] { "plan-empty" });
        }

        foreach (var operation in catalog.Plan.Operations)
        {
            var declared = commands.Describe(operation.Id);
            if (declared is null) missing.Add($"{operation.Id}:kernel-describe-missing");
            else if (!string.Equals(declared.Edge, operation.Edge, StringComparison.Ordinal))
                missing.Add($"{operation.Id}:edge-drift({declared.Edge}!={operation.Edge})");
            else ready.Add(operation.Id);
        }

        return new RelationGatherHostingResult(
            new RelationGatherPoint(commands, queries, catalog, adapters, diagnostics), ready, missing);
    }
}

/// <summary>装配结果（结构计数；未就绪原因逐条可见）。</summary>
public sealed record RelationGatherHostingResult(
    IRelationGatherPoint GatherPoint,
    IReadOnlyList<string> ReadyOperations,
    IReadOnlyList<string> MissingOperations);
