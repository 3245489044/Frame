using System;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>数据存储门面：装配三区（Zones）、快照、checkpoint 与持久化端口。Agent 无关。</summary>
public interface IDataStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);

    IZoneStore Zones { get; }

    ISnapshotStore Snapshots { get; }

    ICheckpointStore Checkpoints { get; }

    /// <summary>配置即数据行（ur_config；改数据行即改行为，对齐 §八/P3）。</summary>
    IConfigStore Config { get; }

    /// <summary>标准化节点数据（ur_nodes；「三种数据」模型的第一种，统一 input/output/event/checkpoint/resource）。</summary>
    INodeStore Nodes { get; }

    /// <summary>替换节点库承载（组合根装配写透守卫用）：把 <see cref="Nodes"/> 换成包装后的写透实现，使后续 ur_nodes 业务节点写自动同步图镜像。</summary>
    void ReplaceNodeStore(INodeStore nodes);

    /// <summary>快照图（唯一快照 + 引用逻辑；基于任务快照的引用查询/引用删除）。</summary>
    ISnapshotGraphStore SnapshotGraph { get; }

    IPersistencePort Port { get; }
}
