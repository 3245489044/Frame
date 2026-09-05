using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>快照引用边的端点类型常量（ur_references 中的 SourceType/TargetType）。</summary>
public static class SnapshotGraphStoreTypes
{
    /// <summary>唯一快照（数据根）。</summary>
    public const string Snapshot = "snapshot";

    /// <summary>标准化节点（数据）。</summary>
    public const string Node = "node";
}

/// <summary>快照↔节点 引用边（ur_references：SourceType='snapshot' → TargetType='node'）。</summary>
public sealed class NodeReference
{
    public long Id { get; set; }

    public string SourceType { get; set; } = SnapshotGraphStoreTypes.Snapshot;

    /// <summary>快照根的复合键（<see cref="UniversalWorkflowFramework.StorageFramework.Core.PersistenceHelper.SnapshotKey"/>）。</summary>
    public string SourceId { get; set; } = string.Empty;

    public string TargetType { get; set; } = SnapshotGraphStoreTypes.Node;

    /// <summary>被引用节点键（<see cref="NodeRecord.NodeKey"/>）。</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>引用类别（如 'belongs'）。</summary>
    public string RefType { get; set; } = "belongs";

    public double Strength { get; set; } = 1.0;
}

/// <summary>快照图：唯一快照（根）+ 其引用的全部标准化节点。</summary>
public sealed class SnapshotGraph
{
    /// <summary>快照根键（<see cref="UniversalWorkflowFramework.StorageFramework.Core.PersistenceHelper.SnapshotKey"/>）。</summary>
    public string SnapshotKey { get; init; } = string.Empty;

    /// <summary>快照根载荷（JSON）；null = 快照根不存在。</summary>
    public string? SnapshotJson { get; init; }

    /// <summary>根通过引用边关联的全部标准化节点（按 Id 升序）。</summary>
    public IReadOnlyList<NodeRecord> Nodes { get; init; } = System.Array.Empty<NodeRecord>();
}

/// <summary>
/// 快照图端口 —— 在标准节点查询之上，为唯一快照（数据根）建立 根↔节点 引用关系：
/// <list type="bullet">
///   <item><b>基于任务快照的引用查询</b>：<see cref="GetGraphAsync"/> 一次取回快照根 + 其全部引用节点。</item>
///   <item><b>基于任务快照的引用删除</b>：<see cref="DeleteGraphAsync"/> 删除快照根 + 引用边 + 全部关联节点（单事务原子）。</item>
/// </list>
/// 对齐「三种数据」模型的第二、三种（唯一快照 + 引用逻辑）。
/// </summary>
public interface ISnapshotGraphStore
{
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>写入/更新唯一快照根（幂等，同 (threadId, sessionId) 覆写）。</summary>
    Task<long> SaveSnapshotAsync(string threadId, string? sessionId, string snapshotJson, CancellationToken ct = default);

    /// <summary>读取快照根载荷；不存在返回 null。</summary>
    Task<string?> LoadSnapshotAsync(string threadId, string? sessionId, CancellationToken ct = default);

    /// <summary>把某节点挂到快照根下（幂等：同 根+节点+RefType 先删后加）。</summary>
    Task AttachNodeAsync(string threadId, string? sessionId, string nodeKey, string? refType = null,
        double strength = 1.0, CancellationToken ct = default);

    /// <summary>基于任务快照的引用查询：取回快照根 + 其全部引用节点。</summary>
    Task<SnapshotGraph> GetGraphAsync(string threadId, string? sessionId, CancellationToken ct = default);

    /// <summary>基于任务快照的引用删除：删除快照根 + 引用边 + 全部关联节点，返回被删除的节点键。</summary>
    Task<IReadOnlyList<string>> DeleteGraphAsync(string threadId, string? sessionId, CancellationToken ct = default);
}
