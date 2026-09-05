using System;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Local;

/// <summary>
/// Local 持久化门面：checkpoint 用内存占位；事务用 <see cref="NoopTransaction"/>。复用 <see cref="PersistenceStoreFacade"/> 门面。
/// 资源/引用不再经骨干暴露（引用关系真源为 LiteDB 图库 graph-log.db，由组合根独立装配，见 ReferenceGraphCoordinator）。
/// </summary>
public sealed class LocalPersistenceStore : PersistenceStoreFacade
{
    public LocalPersistenceStore(PersistenceOptions options, ICheckpointStore checkpointStore, ISnapshotStore snapshotStore)
        : base(
            checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore)),
            schema: null,
            NoopTransaction.Factory,
            snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore)))
    {
        ArgumentNullException.ThrowIfNull(options);
    }
}
