using System;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// Graph 持久化门面：checkpoint 用内存占位（Graph 原生 checkpoint 归 P4）；事务用 <see cref="NoopTransaction"/>。
/// 复用 <see cref="PersistenceStoreFacade"/> 门面。资源/引用不再经骨干暴露（引用关系真源为 LiteDB 图库 graph-log.db，
/// 由组合根独立装配，见 ReferenceGraphCoordinator）。
/// </summary>
public sealed class GraphPersistenceStore : PersistenceStoreFacade
{
    public GraphPersistenceStore(
        ICheckpointStore checkpointStore,
        ISnapshotStore snapshotStore)
        : base(
            checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore)),
            schema: null,
            NoopTransaction.Factory,
            snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore)))
    {
    }
}
