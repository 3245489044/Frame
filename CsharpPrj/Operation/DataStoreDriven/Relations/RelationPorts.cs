using System;
using System.Collections.Generic;

namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 关系端口聚合（唯一租约）：命令 / 查询 / 声明反查三件同源装配。
/// <para>未 Attach 即抛（禁静默回退旧直写）；构造期允许空壳以解开 coordinator→kernel 的构造顺序。</para>
/// </summary>
public interface IRelationPorts
{
    IRelationCommandPort Commands { get; }

    IRelationQueryPort Queries { get; }

    IRelationOperationResolver Operations { get; }

    /// <summary>关系聚拢点（唯一求值入口）；未装配 ⇒ null（调用方回退端口直连）。</summary>
    IRelationGatherPoint? Gather => null;
}

/// <summary>
/// 端口租约提供者：通用编排门面从既有 grounding 句柄取端口（免新增装配点；无反向依赖）。
/// </summary>
public interface IRelationPortsProvider
{
    IRelationPorts Ports { get; }
}

/// <summary>
/// 端口租约实现（唯一装配点 = <c>AgentStorageBundle.RegisterRelationPorts</c>）。
/// <para>读取未装配成员 ⇒ <see cref="InvalidOperationException"/>，消息含 <c>关系端口未装配</c>。</para>
/// </summary>
public sealed class RelationPortLease : IRelationPorts
{
    private IRelationCommandPort? _commands;
    private IRelationQueryPort? _queries;
    private IRelationOperationResolver? _operations;
    private IRelationGatherPoint? _gather;

    /// <summary>三件是否已全部装配。</summary>
    public bool IsAttached
        => _commands is not null && _queries is not null && _operations is not null;

    public IRelationCommandPort Commands => _commands ?? throw NotAttached();

    public IRelationQueryPort Queries => _queries ?? throw NotAttached();

    public IRelationOperationResolver Operations => _operations ?? throw NotAttached();

    /// <summary>聚拢点（可空：未装配则调用方回退端口直连；装配后为唯一求值入口）。</summary>
    public IRelationGatherPoint? Gather => _gather;

    /// <summary>装配聚拢点（唯一维护面；缺省即回退端口直连，兼容旧测试夹具）。</summary>
    public void AttachGather(IRelationGatherPoint gather)
        => _gather = gather ?? throw new ArgumentNullException(nameof(gather));

    /// <summary>装配三件（幂等覆盖；禁止部分装配后静默使用旧值）。</summary>
    public void Attach(
        IRelationCommandPort commands, IRelationQueryPort queries, IRelationOperationResolver operations)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(operations);

        _commands = commands;
        _queries = queries;
        _operations = operations;
    }

    /// <summary>测试夹具：一次装配完毕的租约（禁测试内逐属性赋值）。</summary>
    public static RelationPortLease ForTest(
        IRelationCommandPort commands, IRelationQueryPort queries, IRelationOperationResolver operations)
    {
        var lease = new RelationPortLease();
        lease.Attach(commands, queries, operations);
        return lease;
    }

    private static InvalidOperationException NotAttached()
        => new("关系端口未装配（RelationPortLease.Attach 未调用；唯一装配点 = AgentStorageBundle.RegisterRelationPorts）");
}

/// <summary>
/// 关系声明反查（operation / edge+intent / derive）：唯一数据源 = <see cref="RelationPlanCatalog"/>。
/// <para>0 命中或多义 ⇒ <c>null</c>（fail-closed）；不复制计划、不缓存第二份声明。</para>
/// </summary>
public interface IRelationOperationResolver
{
    /// <summary>按 operation id 取声明（未声明 ⇒ null）。</summary>
    RelationOperation? Resolve(string operation);

    /// <summary>按「边 + 意图」反查唯一 operation（0/多义 ⇒ null）。</summary>
    RelationOperation? ResolveByEdge(string edge, bool remove);

    /// <summary>按 derive id 取派生声明（未声明 ⇒ null）。</summary>
    RelationDerivation? ResolveDerivation(string deriveId);

    /// <summary>边是否已在声明面登记（数据流边不在此列）。</summary>
    bool IsDeclaredEdge(string edge);

    /// <summary>是否为数据流边（声明面未登记但属图内数据流语义；调用方走专用路径）。</summary>
    bool IsDataflowEdge(string edge);
}

/// <inheritdoc />
public sealed class RelationOperationResolver : IRelationOperationResolver
{
    private readonly RelationPlanCatalog _catalog;
    private readonly IRelationPolicy _policy;

    public RelationOperationResolver(RelationPlanCatalog catalog, IRelationPolicy policy)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc />
    public RelationOperation? Resolve(string operation) => _catalog.Resolve(operation);

    /// <inheritdoc />
    public RelationOperation? ResolveByEdge(string edge, bool remove) => _catalog.ResolveByEdge(edge, remove);

    /// <inheritdoc />
    public RelationDerivation? ResolveDerivation(string deriveId) => _catalog.ResolveDerivation(deriveId);

    /// <inheritdoc />
    public bool IsDeclaredEdge(string edge)
        => !string.IsNullOrWhiteSpace(edge) && _policy.IsDeclared(edge);

    /// <inheritdoc />
    public bool IsDataflowEdge(string edge)
        => !string.IsNullOrWhiteSpace(edge) && DataflowEdges.Contains(edge);

    /// <summary>
    /// 数据流边白名单（声明图/数据流语义；不走 <c>[Relation]</c> 归属写）。
    /// <para>归属用法（裸 refType + task 源）必须消灭，仅保留 provenance/键映射语义。</para>
    /// </summary>
    private static readonly HashSet<string> DataflowEdges = new(StringComparer.Ordinal)
    {
        "node_to_node",
        "keymapping_to_node",
    };
}
