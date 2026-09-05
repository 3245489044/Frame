using System;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using Operation.DataStoreDriven.ReferenceSchema;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using UniversalWorkflowFramework.Core.Interfaces;

namespace Operation.DataStoreDriven.Resolvers;

/// <summary>
/// 配置驱动动态解析（组合根）：按 <see cref="PersistenceProvider"/> 解析具体 <see cref="IDataStore"/>。
/// 单一写入口：可传入组合根已注入的 <see cref="IPersistenceStore"/> 骨干（<paramref name="sharedBackbone"/>）复用，
/// 避免双骨干/双真相；缺省自建骨干。
/// 数据驱动层（三区 + 持久化端口）门面：统一经 <see cref="FacadeDataStore"/> 装配。
/// Sqlite 注入持久化三区（<see cref="NodeZoneStore"/>，经节点库落为 NodeType 判别节点）与配置
/// （<see cref="Core.NodeConfigStore"/>）；①基础节点库为 SQLite ur_nodes（<see cref="SqliteNodeStore"/>，键名+JSON+入库时间，与骨干同库同连接）。
/// LiteDB/Local/Graph 复用骨干 + 内存三区占位（<see cref="InMemoryZoneStore"/>，重启即失，仅接线/原型；持久化三区待各后端实现）。
/// 切换后端 = 改 <c>Persistence.Provider</c> 配置，零代码改动。
/// </summary>
public static class DataStoreProviderFactory
{
    /// <summary>从配置对象直接构造 <see cref="IDataStore"/>（供 Agent 编排器 / 测试组合根使用）。
    /// 注入 <paramref name="grounding"/>（引用守卫）时，Sqlite 路径先建 <see cref="WriteGuardPort"/>，
    /// 再以其为底层构造 Zone/Config/Checkpoint/nodes——守卫覆盖整库写面（结构保证，非"Zone 恰不需镜像"的巧合）。</summary>
    public static IDataStore Create(
        PersistenceOptions options,
        IPersistenceStore? sharedBackbone = null,
        IUniversalReferenceManager? referenceManager = null,
        ICheckpointStore? placeholderCheckpoints = null,
        ISnapshotStore? placeholderSnapshots = null,
        IReferenceTypeRegistry? referenceTypeRegistry = null,
        ReferenceGrounding? grounding = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Provider switch
        {
            PersistenceProvider.Sqlite => BuildSqliteDataStore(options, sharedBackbone, grounding),
            PersistenceProvider.LiteDB or PersistenceProvider.Local or PersistenceProvider.Graph
                => BuildFacade(options, sharedBackbone, referenceManager, placeholderCheckpoints, placeholderSnapshots, referenceTypeRegistry),
            _ => throw new NotSupportedException($"未注册的持久化后端: {options.Provider}"),
        };
    }

    private static IDataStore BuildFacade(
        PersistenceOptions options,
        IPersistenceStore? sharedBackbone,
        IUniversalReferenceManager? referenceManager,
        ICheckpointStore? placeholderCheckpoints,
        ISnapshotStore? placeholderSnapshots,
        IReferenceTypeRegistry? referenceTypeRegistry)
    {
        var backbone = sharedBackbone
            ?? PersistenceProviderFactory.Create(options, referenceManager, placeholderCheckpoints, placeholderSnapshots, referenceTypeRegistry);
        // 三区内存占位：非 Sqlite 后端的数据驱动三区暂不持久化（重启即失），待各后端实现持久化三区。
        return new FacadeDataStore(backbone, new InMemoryZoneStore(), new SnapshotPersistencePort(backbone.Snapshots),
            disposeBackbone: sharedBackbone is null);
    }

    private static IDataStore BuildSqliteDataStore(PersistenceOptions options, IPersistenceStore? sharedBackbone, ReferenceGrounding? grounding)
    {
        var factory = new SqliteConnectionFactory(options);
        var backbone = sharedBackbone ?? new SqlitePersistenceStore(factory, options);
        // 三库收敛：①基础节点库为 SQLite ur_nodes（与 Sqlite 骨干同库同连接），极简「键名 + 一个 JSON + 入库时间」，
        //   config/keymapping/checkpoint/input/output/event 均经节点库承载（NodeType 判别），不再单独建 SQL ur_config 等表；
        //   任务（ur_tasks）落同一 SQL 库文件；引用关系真源为 LiteDB 图库（ReferenceGraphCoordinator，组合根独立装配）。
        // 节点库经 AmbientTransactionAwareCommandExecutor 纳入骨干 SQLite 环境事务，与 ur_tasks 单库原子（跨介质隐患消除）。
        var prefix = PersistenceHelper.ResolveTablePrefix(options.TablePrefix);
        // 架构 I2（每库单写通道）：agent 数据驱动基础节点库（ur_nodes，Sqlite 路径即 agent.db）写经 SqliteChannelSession
        // 走 SqliteWriteChannel 单写连接，与 ur_tasks(TaskPoolHosting)/SqliteTransaction(持久化事务) 共用同一 agent.db 写权，
        // 消除 ur_nodes 经 DataUniversal 新连接与节点持久化事务互撞（运行期写锁长持根因）。
        var nodes = new SqliteNodeStore(new SqliteChannelSession(ThreadingSqliteWriteHost.Shared, factory), prefix + TableNames.Nodes);
        // 装配顺序修正（消除"绕组装漂移"）：注入引用守卫时，先建写透守卫，再以守卫为底层构造
        // Zone/Config/Checkpoint/nodes——守卫覆盖整库写面（R2 结构保证），判别类型镜像与否由守卫白名单裁决。
        INodeStore bottom = grounding is null ? nodes : new WriteGuardPort(nodes, grounding);
        return new FacadeDataStore(
            backbone,
            new NodeZoneStore(bottom),
            new SnapshotPersistencePort(backbone.Snapshots),
            config: new NodeConfigStore(bottom),
            disposeBackbone: sharedBackbone is null,
            nodes: bottom,
            // 快照图：.SnapshotGraph 无运行时消费方，缺省回落 InMemorySnapshotGraphStore。
            checkpoints: new NodeCheckpointStore(bottom));
    }
}

