namespace Operation.DataStoreDriven.Relations;

/// <summary>聚拢点意图（写侧）：ensure / remove。</summary>
public enum RelationIntent
{
    Ensure = 0,
    Remove = 1,
}

/// <summary>
/// 关系聚拢点（唯一维护面 / 唯一求值入口，中性无域词）：所有写入口只经本端口。
/// <para>段序固定：解析计划 → 端点 provision 委托 → 探针 → 不变量 → duplicate → 唯一写边 → 诊断。</para>
/// </summary>
public interface IRelationGatherPoint
{
    /// <summary>写：operation 可直给 id，或由 (edge, intent) 反查唯一 operation。</summary>
    Task<RelationResult> InvokeAsync(RelationIntent intent, RelationRequest request, CancellationToken ct = default);

    /// <summary>写（operation id 直给；适配器/域服务的唯一转发面，调用点不持 operation 字面量）。</summary>
    Task<RelationResult> InvokeByOperationAsync(string operation, RelationIntent intent, RelationRequest request, CancellationToken ct = default);

    /// <summary>读：deriveId = [Derive] 的 id；返回键集 + 空因。</summary>
    Task<RelationQueryResult> DeriveAsync(string deriveId, RelationScope scope, CancellationToken ct = default);

    /// <summary>只读声明视图（诊断 / UI / 重建共用）。</summary>
    RelationOperation? Describe(string operation);
}

/// <summary>按边名 + 意图写入（调用点不持 operation 字面量时的唯一入口）。</summary>
public interface IRelationGatherPointByEdge
{
    Task<RelationResult> InvokeByEdgeAsync(string edge, RelationIntent intent, RelationRequest request, CancellationToken ct = default);
}
