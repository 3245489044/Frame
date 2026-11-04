using System;

namespace UniversalWorkflowFramework.Relations.Kernel;

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
    private IRelationMaintenancePort? _maintenance;

    /// <summary>三件是否已全部装配。</summary>
    public bool IsAttached
        => _commands is not null && _queries is not null && _operations is not null;

    public IRelationCommandPort Commands => _commands ?? throw NotAttached();

    public IRelationQueryPort Queries => _queries ?? throw NotAttached();

    public IRelationOperationResolver Operations => _operations ?? throw NotAttached();

    /// <summary>聚拢点（可空：未装配则调用方回退端口直连；装配后为唯一求值入口）。</summary>
    public IRelationGatherPoint? Gather => _gather;

    /// <summary>维护端口（可空：未装配则调用方回退逐条写）。</summary>
    public IRelationMaintenancePort? Maintenance => _maintenance;

    /// <summary>装配维护端口（唯一装配点 = AgentStorageBundle；与命令端口同一 kernel 实例）。</summary>
    public void AttachMaintenance(IRelationMaintenancePort maintenance)
        => _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));

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
