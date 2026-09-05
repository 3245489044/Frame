using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.Pipeline.StaticPipeline;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 数据存储门面（后端无关通用装配）：组合 <see cref="IPersistenceStore"/> 骨干（checkpoint/资源/快照）
/// + <see cref="IZoneStore"/>（三区）+ <see cref="IPersistencePort"/>（持久化端口），实现 <see cref="IDataStore"/>。
/// 是 <see cref="IDataStore"/> 的唯一门面：Sqlite 注入持久化三区/配置，其余后端以内存占位（见 <see cref="InMemoryZoneStore"/>）。
/// </summary>
public sealed class FacadeDataStore : IDataStore
{
    private readonly IPersistenceStore _backbone;
    private readonly bool _disposeBackbone;
    private readonly IZoneStore _zones;
    private readonly IPersistencePort _port;
    private readonly IConfigStore _config;
    private INodeStore _nodes;
    private readonly ISnapshotGraphStore _snapshotGraph;
    private readonly ICheckpointStore _checkpoints;

    /// <param name="disposeBackbone">
    /// 是否由本门面负责释放骨干。传入共享骨干（<c>sharedBackbone</c> 复用）时应为 false，由组合根统一释放，
    /// 避免与 <see cref="DataStoreEngine"/> / 编排器注册的同一骨干重复释放（LiteDB 持 <see cref="LiteDB.LiteDatabase"/> 句柄）。
    /// </param>
    /// <param name="checkpoints">
    /// 可选 checkpoint 覆盖：三库收敛后 Sqlite 路径的 checkpoint 经节点库（<see cref="NodeCheckpointStore"/>）承载，
    /// 不再走骨干的 SQL checkpoint 表；缺省回落 <paramref name="backbone"/>.Checkpoints。
    /// </param>
    public FacadeDataStore(IPersistenceStore backbone, IZoneStore zones, IPersistencePort port,
        IConfigStore? config = null, bool disposeBackbone = true,
        INodeStore? nodes = null, ISnapshotGraphStore? snapshotGraph = null,
        ICheckpointStore? checkpoints = null)
    {
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _disposeBackbone = disposeBackbone;
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _port = port ?? throw new ArgumentNullException(nameof(port));
        // 配置即数据行：非 Sqlite 后端缺省内存占位（重启即失），持久化配置待各后端实现。
        _config = config ?? new InMemoryConfigStore();
        // 「三种数据」模型的节点/快照图：缺省内存占位（非 Sqlite 后端 / 测试）；Sqlite 由组合根注入持久化实现。
        _nodes = nodes ?? new InMemoryNodeStore();
        _snapshotGraph = snapshotGraph ?? new InMemorySnapshotGraphStore();
        _checkpoints = checkpoints ?? _backbone.Checkpoints;
    }

    public IZoneStore Zones => _zones;

    public ISnapshotStore Snapshots => _backbone.Snapshots;

    public ICheckpointStore Checkpoints => _checkpoints;

    public IConfigStore Config => _config;

    public INodeStore Nodes => _nodes;

    /// <summary>替换节点库承载（组合根装配写透守卫用）：把 <see cref="Nodes"/> 换成包装后的写透实现，使后续 ur_nodes 写入统一流经守卫。
    /// 注意：装配期已以原节点库实例构造的 Zone/Config/Checkpoint/SnapshotGraph 组件仍持原实例，但非业务节点写不建图镜像，不受影响。</summary>
    public void ReplaceNodeStore(INodeStore nodes)
    {
        _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    public ISnapshotGraphStore SnapshotGraph => _snapshotGraph;

    public IPersistencePort Port => _port;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // 顺序初始化步骤编排（StaticAsyncPipeline）：backbone→config→zones→nodes→snapshotGraph，
        // 各步骤 IsFatal=true 保持"任一步失败即抛出"语义；StepExecutionException 回抛原始异常。
        try
        {
            await Pipeline.CreateAsync<FacadeDataStore>()
                .AddStep("Backbone", s => s._backbone.InitializeAsync(ct))
                .AddStep("Config", s => s._config.InitializeAsync(ct))
                .AddStep("Zones", s => s._zones.InitializeAsync(ct))
                .AddStep("Nodes", s => s._nodes.InitializeAsync(ct))
                .AddStep("SnapshotGraph", s => s._snapshotGraph.InitializeAsync(ct))
                .Build()
                .ExecuteOrThrowAsync(this).ConfigureAwait(false);
        }
        catch (StepExecutionException ex)
        {
            PipelineExceptionDispatch.RethrowOriginal(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 仅释放自建骨干（共享骨干由组合根统一释放，避免双释放）。
        if (_disposeBackbone)
            await _backbone.DisposeAsync().ConfigureAwait(false);
    }
}
