using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 声明驱动的关系内核：<b>唯一写入口</b>（<see cref="IRelationCommandPort"/>）。
/// <para>算法：plan 解析 → 边声明检查 → 端点构造 → 探针 → 不变量 → duplicate → 唯一写边（UR 端口）。</para>
/// </summary>
public sealed partial class DeclaredRelationKernel : IRelationCommandPort
{
    private readonly IResourceReferenceStore _store;
    private readonly RelationPlanCatalog _catalog;
    private readonly IRelationPolicy _policy;
    private readonly IWorkflowNamespace _namespaces;
    private readonly IEntityExistenceProbe _probe;
    private readonly bool _probeAssembled;
    private readonly RelationInvariantChecker _invariants;
    private readonly IRelationDiagnostics? _diagnostics;
    private readonly IRelationContainerResolver? _containers;

    public DeclaredRelationKernel(
        IResourceReferenceStore store,
        RelationPlanCatalog catalog,
        IRelationPolicy policy,
        IWorkflowNamespace namespaces,
        IEntityExistenceProbe? probe = null,
        IRelationDiagnostics? diagnostics = null,
        IRelationContainerResolver? containers = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _probeAssembled = probe is not null;
        _probe = probe ?? UnassembledEntityExistenceProbe.Instance;
        _diagnostics = diagnostics;
        _containers = containers;
        _invariants = new RelationInvariantChecker(store);
    }

    public RelationOperation? Describe(string operation) => _catalog.Resolve(operation);
}
