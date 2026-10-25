using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Configuration;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.ReferenceSchema;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Resolvers;

/// <summary>
/// 存储核心包（Frame 层产物，<b>禁出现任何 Agent 类型</b>）：
/// 介质 + 节点库 + 图协调器 + 引用守卫 + 跨库写单元 + 一致性守护（不可变）。
/// <para>唯一生产入口 = <see cref="IStorageCompositionRoot"/>；消费方（Agent 组合根）只做领域端口叠加。</para>
/// </summary>
/// <param name="SqlitePath">SQLite 实体库绝对路径（权威）。</param>
/// <param name="GraphPath">LiteDB 关系库绝对路径（可重建派生索引）。</param>
/// <param name="TablePrefix">统一表/集合前缀。</param>
/// <param name="Persistence">持久化骨干（单写入口）。</param>
/// <param name="DataStore">数据存储门面（三区/配置/快照/checkpoint/节点库）。</param>
/// <param name="Nodes">节点库根（已由写透守卫覆盖整库写面）。</param>
/// <param name="Graph">图协调器（唯一 LiteDB owner + Outbox 承载）。</param>
/// <param name="Grounding">引用守卫（边推导/对账/镜像）。</param>
/// <param name="WriteUnit">跨库写单元（SQL→Graph 固定序）。</param>
/// <param name="Consistency">一致性守护（三入口一实现）。</param>
public sealed record StorageCoreBundle(
    string SqlitePath,
    string GraphPath,
    string TablePrefix,
    IPersistenceStore Persistence,
    IDataStore DataStore,
    INodeStore Nodes,
    ReferenceGraphCoordinator Graph,
    IGraphGrounding Grounding,
    ICompositeWriteUnit WriteUnit,
    IStorageConsistencyGuard Consistency)
{
    /// <summary>启动指纹（唯一一条；判据 = 实际打开的两库路径 + 前缀）。</summary>
    public string Fingerprint => $"{SqlitePath}|{GraphPath}|{TablePrefix}";

    /// <summary>图库与实体库同目录（治路径漂移 S8）。</summary>
    public bool SameDirectory =>
        string.Equals(Path.GetDirectoryName(SqlitePath), Path.GetDirectoryName(GraphPath), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 存储组合根（建库/建 store/建守卫/建写单元的唯一入口）；<see cref="Fingerprint"/> 供启动断言。
/// </summary>
public interface IStorageCompositionRoot
{
    /// <summary>创建并初始化存储核心包（幂等：同一实例重复调用返回同一包）。</summary>
    Task<StorageCoreBundle> CreateAsync(CancellationToken ct = default);

    /// <summary>指纹（未创建前为声明的期望值；创建后为实际值）。</summary>
    string Fingerprint { get; }
}

/// <summary>
/// 存储组合根唯一实现：只做「连线」，所有能力复用既有工厂（<see cref="PersistenceProviderFactory"/> /
/// <see cref="DataStoreProviderFactory"/> / <see cref="ReferenceGraphCoordinator"/>），不复制任何存储实现。
/// </summary>
public sealed class StorageCoreCompositionRoot : IStorageCompositionRoot
{
    private readonly PersistenceOptions _options;
    private readonly string _sqlitePath;
    private readonly string _graphPath;
    private readonly string _tablePrefix;
    private readonly IDocumentCodec? _codec;
    private readonly ITableSchemaSource? _schemaSource;
    private readonly IReferenceTypeRegistry? _vocabulary;
    private readonly ReferenceVocabularyGate _vocabularyGate;
    private readonly IReferenceEdgeDiagnostics? _edgeDiagnostics;
    private readonly Func<ReferenceGraphCoordinator, IGraphGrounding> _groundingFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StorageCoreBundle? _bundle;

    public StorageCoreCompositionRoot(
        PersistenceOptions options,
        string sqlitePath,
        string graphPath,
        string tablePrefix,
        IDocumentCodec? codec = null,
        ITableSchemaSource? schemaSource = null,
        IReferenceTypeRegistry? vocabulary = null,
        ReferenceVocabularyGate vocabularyGate = ReferenceVocabularyGate.Report,
        IReferenceEdgeDiagnostics? edgeDiagnostics = null,
        Func<ReferenceGraphCoordinator, IGraphGrounding>? groundingFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlitePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePrefix);
        _sqlitePath = Path.GetFullPath(sqlitePath);
        _graphPath = Path.GetFullPath(graphPath);
        _tablePrefix = tablePrefix;
        _codec = codec;
        _schemaSource = schemaSource;
        _vocabulary = vocabulary;
        _vocabularyGate = vocabularyGate;
        _edgeDiagnostics = edgeDiagnostics;
        // 控制反转：缺省用通用 GraphGrounding（无域语义）；域宿主注入 Domains 的 ReferenceGrounding 门面。
        _groundingFactory = groundingFactory ?? (coordinator => new GraphGrounding(coordinator));
    }

    /// <inheritdoc />
    public string Fingerprint => _bundle?.Fingerprint ?? $"{_sqlitePath}|{_graphPath}|{_tablePrefix}";

    /// <inheritdoc />
    public async Task<StorageCoreBundle> CreateAsync(CancellationToken ct = default)
    {
        if (_bundle is not null) return _bundle;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_bundle is not null) return _bundle;

            // 两库同目录：结构断言（治 S8 路径漂移；不一致即 fail-fast，禁静默分叉）。
            var sqliteDir = Path.GetDirectoryName(_sqlitePath);
            var graphDir = Path.GetDirectoryName(_graphPath);
            if (!string.Equals(sqliteDir, graphDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"两库必须同目录（治路径漂移）：sqlite={_sqlitePath} | graph={_graphPath}");

            PersistenceHelper.EnsureDirectoryFor(_sqlitePath);

            // ① 图库（唯一 owner；同路径复用 ⇒ 禁二次打开 LiteDB）。
            var graph = ReferenceGraphStoreRegistry.GetOrCreate(_graphPath, p => ReferenceGraphCoordinator.CreateAsync(
                p, _tablePrefix, ct,
                vocabulary: _vocabulary,
                vocabularyGate: _vocabularyGate,
                edgeDiagnostics: _edgeDiagnostics,
                codec: _codec));

            // ② 引用守卫（边推导/对账/镜像）：通用面由核心提供，域宿主经工厂注入完整门面。
            var grounding = _groundingFactory(graph);

            // ③ 持久化骨干 + 数据驱动层（复用骨干；守卫覆盖整库写面）。
            var persistence = PersistenceProviderFactory.Create(_options, codec: _codec, schemaSource: _schemaSource);
            await persistence.InitializeAsync(ct).ConfigureAwait(false);
            var dataStore = DataStoreProviderFactory.Create(
                _options, sharedBackbone: persistence, grounding: grounding,
                codec: _codec, schemaSource: _schemaSource);

            // ④ 写单元 + 一致性守护（Outbox 与图同库同 owner ⇒ 不引入第二介质）。
            var writeUnit = new CompositeWriteUnit(graph);
            var consistency = new StorageConsistencyGuard(grounding, graph, writeUnit);

            _bundle = new StorageCoreBundle(
                _sqlitePath, _graphPath, _tablePrefix,
                persistence, dataStore, dataStore.Nodes, graph, grounding, writeUnit, consistency);
            return _bundle;
        }
        finally
        {
            _gate.Release();
        }
    }
}
