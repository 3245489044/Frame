using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using LiteDB;
using Modules.GraphProcessing.Algorithms;
using Modules.Node.Relations;
using Operation.DataStoreDriven.Abstractions;
using ReferenceTypes = Operation.DataStoreDriven.Abstractions.ReferenceTypes;
using Operation.DataStoreDriven.Configuration;
using Operation.DataStoreDriven.ReferenceSchema;
using Plate.DslMethod.Usharp;

using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用关系图协调器 —— 唯一读写 LiteDB 引用关系的组件。
/// 设计原则（LiteDB = 唯一逻辑关系持有库，设计 §2）：
///   · 三集合：ur_references（引用边）+ ur_endpoints（端点）+ ur_relations（关系声明，G1 起唯一声明权威）；
///   · SQL 拥有实体（ur_tasks / ur_nodes 存 task/node 本体与载荷），本协调器**不在 LiteDB 复制实体数据**；
///   · 端点存在性由 LiteDB 自持（D1）；本协调器不 fail-fast 校验端点，直接接受任意端点键；
///   · 会话内内存维护最小端点注册表（供引用完整性校验与 UI 展示端点符号名），并从 ur_endpoints 重建；
///   · 关系声明写/读一律经 ur_relations（USharp [Record]，_id = SQL NodeKey 文法），SQL 不再持有声明。
/// 本类同时实现 <see cref="IResourceReferenceStore"/> 与 <see cref="INodeRelationStore"/> 端口，保持既有消费方（编排器/对账器）编译兼容。
/// </summary>
public sealed class ReferenceGraphCoordinator : IResourceReferenceStore, INodeRelationStore, IGraphCatalogReader, IOutboxStore, IAsyncDisposable
{
    private readonly LiteDatabase _db;                                    // 唯一持有句柄
    private readonly ILiteCollection<BsonDocument> _edges;                // ur_references（唯一集合，纯边）
    private readonly ILiteCollection<BsonDocument> _endpointDocs;         // ur_endpoints（端点符号/metadata 落盘缓存）
    private readonly ILiteCollection<BsonDocument> _relationDocs;         // ur_relations（关系声明真源；权威=LiteDB，G1）
    private readonly ILiteCollection<BsonDocument> _tombstones;           // ur_graph_tombstones（删除意图；先墓碑后物理删除）
    private readonly ILiteCollection<BsonDocument> _outbox;               // ur_write_outbox（跨库写单元重放；与边同库，走单写门）
    private readonly ICoreGraph<string, Resource, ReferenceEdgeData> _graph; // 热邻接（内存，由边重建）
    private readonly Dictionary<string, Resource> _endpoints;             // 会话内存端点注册表（由 ur_endpoints + 边重建）
    private readonly Dictionary<string, string> _typeById;                // "{type}:{id}" -> key（端点 O(1) 查找；对齐 GetUniqueKey）
    private readonly SemaphoreSlim _gate = new(1, 1);                     // 单写
    // 词表武装（B3）：写边前校验边类型是否在词表 DSL 声明（未装配 ⇒ 不校验，零回归）。
    private readonly IReferenceTypeRegistry? _vocabulary;
    private readonly ReferenceVocabularyGate _vocabularyGate;
    // 文档编解码端口（G1）：边/端点文档 = { _id, Doc }，Doc = USharp [Record] 文本真源（组合根注入 UsharpDocumentCodec）。
    private readonly IDocumentCodec _codec;
    private readonly IReferenceEdgeDiagnostics _edgeDiagnostics;
    private bool _initialized;
    private int _disposed;
    private long _version;
    private long _membershipVersion;

    /// <summary>
    /// 图变更版本（单调递增）：任意写边/端点/声明成功后 +1。
    /// 供快照缓存做「读前=读后」一致性校验与版本失效（F2），不参与写仲裁。
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// 成员变更版本（单调递增）：仅「影响树成员」的写（<c>history_owns_task</c> 等归属边）推进。
    /// <para>与 <see cref="Version"/> 分离，避免 outbox/tombstone/端点镜像等无关写把树缓存整体失效（RED-11）。</para>
    /// </summary>
    public long MembershipVersion => Interlocked.Read(ref _membershipVersion);

    /// <summary>边文档主键（_id）分隔符（单一权威）。端点键已用 <see cref="ResourceIdentifier.UniqueKeySeparator"/>（":"），
    /// 边键在其上再叠加本分隔符，避免与端点键内的 ":" 冲突；原实现用不可打印控制符 \u0001，重构为可读/可 grep 的 "::"。</summary>
    private const string EdgeKeySeparator = "::";

    /// <summary>Outbox 集合后缀（与边同库同 owner ⇒ 重放不跨库事务；前缀由组合根统一加）。</summary>
    private const string OutboxCollectionSuffix = "write_outbox";

    // 图行编解码（G-4 阶段二：**必填**，无默认回退）：图行 EdgeDoc/EndpointDoc 为非 [Record] 记录通道，
    // 由组合根经 UsharpDataPorts.Codec 显式注入；未注入 ⇒ fail-fast（禁静默回退 JSON）。旧行读兼容由注入 codec 承担。


    private ReferenceGraphCoordinator(
        LiteDatabase db, string edgesCollection, string endpointsCollection, string relationsCollection,
        string tombstonesCollection, string outboxCollection,
        IReferenceTypeRegistry? vocabulary = null,
        ReferenceVocabularyGate vocabularyGate = ReferenceVocabularyGate.Report,
        IReferenceEdgeDiagnostics? edgeDiagnostics = null,
        IDocumentCodec? codec = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _edges = _db.GetCollection<BsonDocument>(edgesCollection);
        _endpointDocs = _db.GetCollection<BsonDocument>(endpointsCollection);
        _relationDocs = _db.GetCollection<BsonDocument>(relationsCollection);
        _tombstones = _db.GetCollection<BsonDocument>(tombstonesCollection);
        _outbox = _db.GetCollection<BsonDocument>(outboxCollection);
        _graph = new CoreGraph<string, Resource, ReferenceEdgeData>();
        _endpoints = new Dictionary<string, Resource>(StringComparer.Ordinal);
        _typeById = new Dictionary<string, string>(StringComparer.Ordinal);
        _vocabulary = vocabulary;
        _vocabularyGate = vocabularyGate;
        _codec = codec ?? throw new ArgumentNullException(nameof(codec),
            "图行编解码为必填（G-4 阶段二）：组合根须注入 UsharpDataPorts.Codec；未注入 ⇒ fail-fast，禁静默回退 JSON。");
        _edgeDiagnostics = edgeDiagnostics ?? NullReferenceEdgeDiagnostics.Instance;
    }

    /// <summary>组合根工厂：建目录 → 自开自持 LiteDatabase → 载入持久端点 + 边重建内存邻接。失败则释放回滚。</summary>
    /// <param name="databasePath">LiteDB 图库路径。</param>
    /// <param name="tablePrefix">表名前缀。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="vocabulary">词表 DSL 投影（可空 = 未装配 ⇒ 不做词表校验，行为与引入前逐字一致）。</param>
    /// <param name="vocabularyGate">词表校验姿态（缺省报告模式；切拒绝模式须待 DSL 覆盖 ⊇ 实际边集）。</param>
    /// <param name="edgeDiagnostics">可见化出口（缺省静默；拒绝模式装配时应同时给出口）。</param>
    public static ReferenceGraphCoordinator CreateAsync(
        string databasePath, string tablePrefix = "ur_", CancellationToken ct = default,
        IReferenceTypeRegistry? vocabulary = null,
        ReferenceVocabularyGate vocabularyGate = ReferenceVocabularyGate.Report,
        IReferenceEdgeDiagnostics? edgeDiagnostics = null,
        IDocumentCodec? codec = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (codec is null)
            throw new ArgumentNullException(nameof(codec),
                "图行编解码为必填（G-4 阶段二）：须注入 UsharpDataPorts.Codec。");
        var prefix = PersistenceHelper.ResolveTablePrefix(tablePrefix);
        var path = PersistenceHelper.ResolveDatabasePath(databasePath, null)!;
        PersistenceHelper.EnsureDirectoryFor(path);
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.LiteDB,
            DatabasePath = databasePath,
            TablePrefix = tablePrefix,
        };
        var conn = PersistenceHelper.BuildConnectionString(PersistenceProvider.LiteDB, options, null);
        var db = new LiteDatabase(conn);
        var coordinator = new ReferenceGraphCoordinator(
            db, prefix + TableNames.References, prefix + TableNames.Endpoints, prefix + TableNames.Relations,
            prefix + TableNames.Tombstones, prefix + OutboxCollectionSuffix,
            vocabulary, vocabularyGate, edgeDiagnostics, codec);
        try
        {
            coordinator.Initialize();
            return coordinator;
        }
        catch
        {
            coordinator.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 把 refType 归一为**词表边名**（B3）：节点关系边形如 <c>node_to_node:{from}&gt;{to}?km=…</c>
    /// 带端口/km 后缀 ⇒ 词表查名须取前缀；其余（归属边 / 工作流静态边）为裸类型名，原样返回。
    /// </summary>
    private static string ResolveEdgeName(string refType)
    {
        if (refType.StartsWith(ReferenceTypes.NodeToNode, StringComparison.Ordinal)) return ReferenceTypes.NodeToNode;
        if (refType.StartsWith(ReferenceTypes.KeyMappingToNode, StringComparison.Ordinal)) return ReferenceTypes.KeyMappingToNode;
        return refType;
    }

    /// <summary>词表准入判定（B3）：未装配词表 ⇒ 放行；已废弃 ⇒ 恒拒（禁新增引用）；未声明 ⇒ 按姿态放行/拒写 + 可见化。</summary>
    /// <returns>true = 放行写边；false = 拒写（归因已经诊断出口可见化）。</returns>
    private bool AllowByVocabulary(string refType, string subject)
    {
        if (_vocabulary is null) return true;

        var edgeName = ResolveEdgeName(refType);
        if (_vocabulary.IsDeprecated(edgeName))
        {
            _edgeDiagnostics.EdgeUndeclared($"{edgeName}(deprecated)", subject);
            return false; // 废弃边恒拒（两种姿态同判；对齐 UniversalReference 适配器的「废弃类型拒绝」）
        }
        if (_vocabulary.IsDeclared(edgeName)) return true;

        _edgeDiagnostics.EdgeUndeclared(edgeName, subject);
        return _vocabularyGate == ReferenceVocabularyGate.Report;
    }

    private void Initialize()
    {
        if (_initialized) return;
        // 查询/级联/分页索引（顶层投影字段；重建库时一次建齐）。
        _edges.EnsureIndex("SourceId");
        _edges.EnsureIndex("TargetId");
        _edges.EnsureIndex("RefType");
        _relationDocs.EnsureIndex("RelationId");
        _relationDocs.EnsureIndex("WorkflowId");
        _endpointDocs.EnsureIndex("ResourceType");
        _endpointDocs.EnsureIndex("Id");
        _tombstones.EnsureIndex("Kind");
        _endpoints.Clear();
        _typeById.Clear();
        // ① 载入持久端点（ur_endpoints；重启保留符号/metadata，避免造裸资源退化图可读性）。旧数据无该集合 → 空集，走纯边兜底。
        foreach (var doc in _endpointDocs.FindAll())
        {
            var key = doc["_id"].IsString ? doc["_id"].AsString : null;
            if (key is null) continue;
            var e = ReadEndpointDoc(doc);
            if (e is null) continue;
            var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
            if (e.Metadata is { Count: > 0 })
                foreach (var kv in e.Metadata) metadata[kv.Key] = kv.Value;
            var resource = new Resource
            {
                Identifier = new ResourceIdentifier { ResourceType = e.ResourceType, Id = e.Id },
                Symbol = string.IsNullOrEmpty(e.Symbol) ? e.Id : e.Symbol,
                Metadata = metadata,
            };
            _endpoints[key] = resource;
            _typeById[KeyOfType(resource.Identifier)] = key;
        }
        // ② 由边重建内存邻接（端点优先用持久符号，缺失则 KeyToIdentifier 造裸资源兜底）。
        foreach (var edge in _edges.FindAll())
        {
            var e = ReadEdgeDoc(edge);
            if (e is null) continue;
            if (string.IsNullOrWhiteSpace(e.SourceId) || string.IsNullOrWhiteSpace(e.TargetId)) continue;
            if (string.IsNullOrWhiteSpace(e.RefType)) continue;
            EnsureNodeInMemory(e.SourceId);
            EnsureNodeInMemory(e.TargetId);
            _graph.AddEdge(BuildEdgeId(e.SourceId, e.TargetId, e.RefType), e.SourceId, e.TargetId, isDirected: true,
                new ReferenceEdgeData(e.RefType, e.Strength), e.Strength);
        }
        _initialized = true;
    }

    /// <summary>读文档 <c>Doc</c> 文本（{ _id, Doc } 形态；无 Doc ⇒ null）。</summary>
    private static string? DocText(BsonDocument doc)
        => doc.TryGetValue("Doc", out var v) && v.IsString ? v.AsString : null;

    /// <summary>由边文档还原 <see cref="EdgeDoc"/>：记录实例端口（<c>[Record]</c> 真源；G4 去旧 JSON 行回退）。</summary>
    private EdgeDoc? ReadEdgeDoc(BsonDocument doc)
    {
        var text = DocText(doc);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return _codec.TryDeserializeRecord(text, out _, out _, out _, out var body) ? EdgeFromRecord(body) : null;
    }

    /// <summary>由端点文档还原 <see cref="EndpointDoc"/>：记录实例端口（G4 去旧 JSON 行回退）。</summary>
    private EndpointDoc? ReadEndpointDoc(BsonDocument doc)
    {
        var text = DocText(doc);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return _codec.TryDeserializeRecord(text, out _, out _, out _, out var body) ? EndpointFromRecord(body) : null;
    }

    /// <summary>读记录标量文本（缺省/null ⇒ null）。</summary>
    private static string? S(UsharpRecord body, string name)
        => body.TryGet(name, out var v) && v is UsharpScalar sc && sc.Kind != UsharpScalarKind.Null ? sc.Text : null;

    private static double D(UsharpRecord body, string name, double fallback)
        => body.TryGet(name, out var v) && v is UsharpScalar { Kind: UsharpScalarKind.Decimal or UsharpScalarKind.Int } sc
           && double.TryParse(sc.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : fallback;

    /// <summary>EdgeDoc → USharp 记录体（字段名 camelCase；写/读同源）。</summary>
    private static UsharpRecord EdgeRecord(EdgeDoc e) => new(new[]
    {
        new KeyValuePair<string, UsharpValue>("sourceId", UsharpScalar.Str(e.SourceId)),
        new KeyValuePair<string, UsharpValue>("sourceType", UsharpScalar.Str(e.SourceType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("targetId", UsharpScalar.Str(e.TargetId)),
        new KeyValuePair<string, UsharpValue>("targetType", UsharpScalar.Str(e.TargetType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("refType", UsharpScalar.Str(e.RefType)),
        new KeyValuePair<string, UsharpValue>("strength", new UsharpScalar(UsharpScalarKind.Decimal, e.Strength.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        new KeyValuePair<string, UsharpValue>("origin", UsharpScalar.Str(e.Origin ?? EdgeOrigin.Runtime)),
    });

    private static EdgeDoc EdgeFromRecord(UsharpRecord body) => new()
    {
        SourceId = S(body, "sourceId") ?? string.Empty,
        SourceType = S(body, "sourceType"),
        TargetId = S(body, "targetId") ?? string.Empty,
        TargetType = S(body, "targetType"),
        RefType = S(body, "refType") ?? string.Empty,
        Strength = D(body, "strength", 1.0),
        Origin = S(body, "origin") ?? EdgeOrigin.Runtime,
    };

    /// <summary>EndpointDoc → USharp 记录体（metadata 为嵌套记录，键序 = 原字典序）。</summary>
    private static UsharpRecord EndpointRecord(string key, Resource resource, ResourceIdentifier id)
    {
        var fields = new List<KeyValuePair<string, UsharpValue>>(5)
        {
            new("resourceType", UsharpScalar.Str(id.ResourceType ?? string.Empty)),
            new("id", UsharpScalar.Str(id.Id ?? string.Empty)),
            new("symbol", UsharpScalar.Str(resource.Symbol ?? string.Empty)),
            new("updatedAt", UsharpScalar.Str(DateTime.UtcNow.ToString("O"))),
        };
        if (resource.Metadata is { Count: > 0 })
        {
            var mf = new List<KeyValuePair<string, UsharpValue>>(resource.Metadata.Count);
            foreach (var kv in resource.Metadata)
                mf.Add(new KeyValuePair<string, UsharpValue>(kv.Key, UsharpScalar.Str(Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)));
            fields.Add(new KeyValuePair<string, UsharpValue>("metadata", new UsharpRecord(mf)));
        }
        return new UsharpRecord(fields);
    }

    private static EndpointDoc EndpointFromRecord(UsharpRecord body)
    {
        Dictionary<string, string>? metadata = null;
        if (body.TryGet("metadata", out var mv) && mv is UsharpRecord mr && mr.Fields.Count > 0)
        {
            metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in mr.Fields) metadata[kv.Key] = (kv.Value as UsharpScalar)?.Text ?? string.Empty;
        }
        return new EndpointDoc
        {
            ResourceType = S(body, "resourceType") ?? string.Empty,
            Id = S(body, "id") ?? string.Empty,
            Symbol = S(body, "symbol"),
            UpdatedAt = S(body, "updatedAt"),
            Metadata = metadata,
        };
    }

    // ---- IResourceReferenceStore 资源方法：仅内存端点注册表（SQL 为实体真源，LiteDB 不复制）----

    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Initialize();
        return Task.CompletedTask;
    }

    /// <summary>注册/覆盖一个端点（会话内存 + ur_endpoints 落盘符号/metadata；实体持久化由 SQL 负责）。</summary>
    public Task AddResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null,
        string? symbol = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        var key = resourceId.GetUniqueKey();
        var resource = ToResource(resourceId, metadata, symbol);
        _endpoints[key] = resource;
        _typeById[KeyOfType(resourceId)] = key;
        PersistEndpointDoc(key, resource, resource.Identifier);
        EnsureNodeInMemory(key);
        return Task.CompletedTask;
    }

    public Task UpdateResourceAsync(ResourceIdentifier resourceId, IReadOnlyDictionary<string, object>? metadata = null,
        string? symbol = null, CancellationToken ct = default)
        => AddResourceAsync(resourceId, metadata, symbol, ct); // upsert 语义（实体数据以 SQL 为准，此处仅端点符号）

    public Task<Resource?> GetResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        return Task.FromResult(_endpoints.TryGetValue(resourceId.GetUniqueKey(), out var r) ? r : null);
    }

    public Task<IReadOnlyList<Resource>> GetAllResourcesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Resource>>(_endpoints.Values.ToList());

    /// <summary>删除资源端点的**派生数据**（端点文档 + 全部边）；**保留**关系声明（声明为源数据 ⇒ 对账 / 重建依赖其存活）。
    /// <para>对账（reconcile / diff）与图对账路径用之；删**实体**（node / task / history）请用
    /// <see cref="DeleteEndpointDurablyAsync"/>（含声明级联 + 墓碑）。</para></summary>
    public Task RemoveResourceAsync(ResourceIdentifier resourceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        var key = resourceId.GetUniqueKey();
        _endpoints.Remove(key);
        _typeById.Remove(KeyOfType(resourceId));
        _endpointDocs.Delete(key);
        if (_graph.ContainsNode(key))
        {
            _graph.RemoveNode(key); // 级联清边（内存邻接）
            _edges.DeleteMany(Query.Or(Query.EQ("SourceId", key), Query.EQ("TargetId", key))); // durable 级联
        }
        return Task.CompletedTask;
    }

    /// <summary>声明是否引用给定资源（(ResourceType, Id) 二元组匹配；Source / Target 任一侧命中）。</summary>
    private static bool ReferencesResource(RelationDoc d, ResourceIdentifier r)
        => (string.Equals(d.Source, r.Id, StringComparison.Ordinal)
                && string.Equals(d.SourceType, r.ResourceType, StringComparison.Ordinal))
           || (string.Equals(d.Target, r.Id, StringComparison.Ordinal)
                && string.Equals(d.TargetType, r.ResourceType, StringComparison.Ordinal));

    /// <summary>幂等确保端点注册（会话内存；SQL 为实体真源）。AddResourceAsync 为 upsert 无重复异常，直接委托。</summary>
    public Task EnsureResourceAsync(ResourceIdentifier resourceId, string? symbol = null,
        IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
        => AddResourceAsync(resourceId, metadata, symbol, ct);
    // ---- IResourceReferenceStore 引用方法：LiteDB 持久边（唯一集合）----

    public Task AddReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType,
        double strength = 1.0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        if (string.IsNullOrWhiteSpace(refType)) throw new ArgumentException("refType 不能为空", nameof(refType));

        var sourceKey = sourceId.GetUniqueKey();
        var targetKey = targetId.GetUniqueKey();

        // 词表准入（B3）：未声明边类型按姿态处置（报告模式放行 + 可见化 / 拒绝模式拒写）；已废弃恒拒（禁静默建边）。
        if (!AllowByVocabulary(refType, sourceKey)) return Task.CompletedTask;

        // LiteDB 只维护关系（task/node 引用边），端点是 SQL 实体（ur_tasks / ur_nodes），存在性由 SQL 写透时序保证；
        // 故协调器不在此 fail-fast 校验端点，直接接受任意端点键（避免关系推导路径对未显式注册的上游端点抛错）。
        return MutateAsync(ct, () =>
        {
            EnsureNodeInMemory(sourceKey);
            EnsureNodeInMemory(targetKey);
            var edgeId = BuildEdgeId(sourceKey, targetKey, refType);
            // durable：先删后加（幂等 upsert，避免重复边）；{ _id, Doc } 形态下按 _id 定位。
            _edges.DeleteMany(Query.EQ("_id", edgeId));
            _edges.Insert(ToEdgeDoc(sourceKey, sourceId, targetKey, targetId, refType, strength, EdgeOrigin.Runtime));
            if (!_graph.ContainsEdge(edgeId))
                _graph.AddEdge(edgeId, sourceKey, targetKey, isDirected: true, new ReferenceEdgeData(refType, strength), strength);
            return Task.CompletedTask;
        }, affectsMembership: AffectsMembership(refType));
    }

    public Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType,
        CancellationToken ct = default)
    {
        var sourceKey = sourceId.GetUniqueKey();
        var targetKey = targetId.GetUniqueKey();
        return MutateAsync(ct, () =>
        {
            var edgeId = BuildEdgeId(sourceKey, targetKey, refType);
            _edges.DeleteMany(Query.EQ("_id", edgeId));
            if (_graph.ContainsEdge(edgeId)) _graph.RemoveEdge(edgeId);
            return Task.CompletedTask;
        }, affectsMembership: AffectsMembership(refType));
    }

    public Task<IReadOnlyList<ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId,
        bool includeIncoming = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        var key = resourceId.GetUniqueKey();
        return Task.FromResult<IReadOnlyList<ResourceReference>>(ReadLocked(ct, () => GetReferencesCore(key, includeIncoming)));
    }

    /// <summary>
    /// 批量读引用（F3）：一次持锁遍历多个端点的邻接，键 = 端点 <see cref="ResourceIdentifier.GetUniqueKey"/>。
    /// 缺失端点 ⇒ 空列表（不伪造）；单次锁内完成，避免 N 次门等待。
    /// </summary>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceReference>>> GetReferencesManyAsync(
        IReadOnlyCollection<ResourceIdentifier> resourceIds,
        bool includeIncoming = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        var ids = resourceIds
            .Where(static id => id is not null)
            .Select(static id => id.GetUniqueKey())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = new Dictionary<string, IReadOnlyList<ResourceReference>>(ids.Count, StringComparer.Ordinal);
        if (ids.Count == 0) return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceReference>>>(result);

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceReference>>>(
            ReadLocked(ct, () =>
            {
                foreach (var key in ids)
                    result[key] = GetReferencesCore(key, includeIncoming);
                return result;
            }));
    }

    /// <summary>未加锁读面核心：枚举内存图邻接为引用列表。调用方须已持 <see cref="_gate"/>（经 <see cref="ReadLocked{T}"/>）。</summary>
    private List<ResourceReference> GetReferencesCore(string key, bool includeIncoming)
    {
        var result = new List<ResourceReference>();
        foreach (var e in _graph.GetOutgoingEdges(key))
            result.Add(new ResourceReference
            {
                Source = KeyToIdentifier(e.Source),
                Target = KeyToIdentifier(e.Target),
                RefType = e.Data?.RefType ?? string.Empty,
                Strength = e.Weight,
                IsIncoming = false,
            });
        if (includeIncoming)
            foreach (var e in _graph.GetIncomingEdges(key))
                result.Add(new ResourceReference
                {
                    Source = KeyToIdentifier(e.Source),
                    Target = KeyToIdentifier(e.Target),
                    RefType = e.Data?.RefType ?? string.Empty,
                    Strength = e.Weight,
                    IsIncoming = true,
                });
        return result;
    }


    // ---- INodeRelationStore（refType 端口/km 编解码唯一实现 = NodeRelationRefCodec，A2 上移公开）----

    public Task EnsureNodeAsync(string workflowId, string nodeKey, string? symbol = null,
        IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        var id = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        return AddResourceAsync(id, metadata, symbol ?? nodeKey, ct);
    }

    /// <summary>写入一条节点关系（幂等：同类型同端点先删后加）。refType 端口/km 编码收拢于此。</summary>
    public Task AddRelationAsync(string workflowId, NodeRelation relation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var src = new ResourceIdentifier { ResourceType = relation.SourceResourceType, Id = relation.SourceId, Location = workflowId };
        var tgt = new ResourceIdentifier { ResourceType = relation.TargetResourceType, Id = relation.TargetId, Location = workflowId };
        return AddReferenceAsync(src, tgt, NodeRelationRefCodec.Encode(relation), relation.Strength, ct);
    }

    public Task RemoveRelationAsync(string workflowId, NodeRelation relation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var src = new ResourceIdentifier { ResourceType = relation.SourceResourceType, Id = relation.SourceId, Location = workflowId };
        var tgt = new ResourceIdentifier { ResourceType = relation.TargetResourceType, Id = relation.TargetId, Location = workflowId };
        return RemoveReferenceAsync(src, tgt, NodeRelationRefCodec.Encode(relation), ct);
    }


    public Task<IReadOnlyList<NodeRelation>> GetRelationsAsync(string workflowId, string nodeKey,
        NodeRelationType? type = null, bool includeIncoming = true, CancellationToken ct = default)
    {
        var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        var nodeIdKey = nodeId.GetUniqueKey();
        return Task.FromResult<IReadOnlyList<NodeRelation>>(ReadLocked(ct, () => GetRelationsCore(nodeIdKey, type, includeIncoming)));
    }

    /// <summary>未加锁读面核心：由引用边映射节点关系（refType 端口/km 编解码）。调用方须已持 <see cref="_gate"/>（经 <see cref="ReadLocked{T}"/>）。</summary>
    private List<NodeRelation> GetRelationsCore(string nodeIdKey, NodeRelationType? type, bool includeIncoming)
    {
        var result = new List<NodeRelation>();
        var refs = GetReferencesCore(nodeIdKey, includeIncoming);
        foreach (var r in refs)
        {
            var parsed = ParseNodeRelationType(r.RefType);
            if (parsed is null) continue;
            if (type is { } t && parsed.Value != t) continue;
            if (parsed == NodeRelationType.NodeToNode)
            {
                var (fromPort, toPort, km) = NodeRelationRefCodec.ParseNodeToNode(r.RefType);
                result.Add(new NodeRelation
                {
                    Type = parsed.Value,
                    SourceResourceType = r.Source.ResourceType ?? string.Empty,
                    SourceId = r.Source.Id ?? string.Empty,
                    SourcePort = fromPort,
                    TargetResourceType = r.Target.ResourceType ?? string.Empty,
                    TargetId = r.Target.Id ?? string.Empty,
                    TargetPort = toPort,
                    KeyMappingRef = km,
                    Strength = r.Strength,
                });
            }
            else
            {
                result.Add(new NodeRelation
                {
                    Type = parsed.Value,
                    SourceResourceType = r.Source.ResourceType ?? string.Empty,
                    SourceId = r.Source.Id ?? string.Empty,
                    TargetResourceType = r.Target.ResourceType ?? string.Empty,
                    TargetId = r.Target.Id ?? string.Empty,
                    Strength = r.Strength,
                });
            }
        }
        return result;
    }

    public Task<IReadOnlyList<string>> GetRelatedNodesAsync(string workflowId, string nodeKey,
        NodeRelationType type, bool outgoing = true, CancellationToken ct = default)
    {
        var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
        var nodeIdKey = nodeId.GetUniqueKey();
        // 端点 Id 前缀 = 键文法单源（DS-P1b-2 收口第 5 处：原 `workflowId + "."` 字面量去除，切片口径与旧 Substring 逐字等价）。
        var nodePrefix = workflowId + Modules.Node.Identity.NodeEndpointIds.Separator;
        return Task.FromResult<IReadOnlyList<string>>(ReadLocked(ct, () =>
        {
            var relations = GetRelationsCore(nodeIdKey, type, includeIncoming: !outgoing);
            var related = new List<string>();
            foreach (var rel in relations)
            {
                var neighborIsSource = !outgoing;
                var other = neighborIsSource ? rel.SourceId : rel.TargetId;
                var otherType = neighborIsSource ? rel.SourceResourceType : rel.TargetResourceType;
                if (otherType != NodeResourceTypes.Node) continue;
                if (string.IsNullOrEmpty(other)) continue;
                // 返回契约 = 节点键（同工作流前缀裁剪）；非前缀端点（跨工作流脏端点）不透出原文，仅可见化后跳过。
                if (!other.StartsWith(nodePrefix, StringComparison.Ordinal))
                {
                    _edgeDiagnostics.NodeKeyOutOfScope(other, workflowId);
                    continue;
                }
                related.Add(other[nodePrefix.Length..]);
            }
            return (IReadOnlyList<string>)related.Distinct(StringComparer.Ordinal).ToList();
        }));
    }


    /// <summary>对账用规范化关系键（diff 依据；端口 / KeyMapping 参与，同端点不同端口可区分）。</summary>
    internal static string RelationKey(NodeRelation r)
        => $"{r.Type}|{r.SourceResourceType}|{r.SourceId}|{r.SourcePort}|{r.TargetResourceType}|{r.TargetId}|{r.TargetPort}|{r.KeyMappingRef}";

    /// <summary>task 写透：注册 task 端点（内存）+ 与关联节点建 task→node 边（缺省 <see cref="ReferenceTypes.NodeToNode"/>）。幂等。
    /// 不整段包外层门锁：内层 AddResource/AddReference 各自经 <see cref="MutateAsync"/> 串行取门锁，避免「持锁内同步重入 SemaphoreSlim」自死锁。</summary>
    public async Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys,
        string workflowId = "", CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var taskId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey, Location = workflowId };
        await AddResourceAsync(taskId, symbol: taskKey, ct: ct).ConfigureAwait(false);
        foreach (var nodeKey in relatedNodeKeys)
        {
            if (string.IsNullOrWhiteSpace(nodeKey) || string.Equals(nodeKey, taskKey, StringComparison.Ordinal)) continue;
            var nodeId = new ResourceIdentifier { ResourceType = NodeResourceTypes.Node, Id = NodeRelationFactory.NodeId(workflowId, nodeKey), Location = workflowId };
            await AddResourceAsync(nodeId, symbol: nodeKey, ct: ct).ConfigureAwait(false);
            await AddReferenceAsync(taskId, nodeId, ReferenceTypes.NodeToNode, 1.0, ct).ConfigureAwait(false);
        }
    }

    /// <summary>task 删除写透：级联移除 task 端点、其全部边与**其关系声明**（实体删除 ⇒ 声明级联，单向 + 墓碑）。</summary>
    public Task RemoveTaskAsync(string taskKey, CancellationToken ct = default)
        => DeleteEndpointDurablyAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, "task-removed", ct);

    /// <summary>
    /// 清空图（全量重建原语）：删除派生边/端点文档 + 清空会话内存端点注册表/邻接；
    /// **保留** ur_relations 声明与墓碑（删除意图不因重建消失）。幂等可重复。
    /// </summary>
    public Task ClearAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return MutateAsync(ct, () =>
        {
            _edges.DeleteAll();
            _endpointDocs.DeleteAll();
            _endpoints.Clear();
            _typeById.Clear();
            foreach (var key in _graph.GetAllNodeKeys().ToList())
                _graph.RemoveNode(key);
            return Task.CompletedTask;
        });
    }

    /// <summary>完全重置图库（含墓碑）：仅维护命令「清空全部」使用。</summary>
    public Task ClearAllIncludingTombstonesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return MutateAsync(ct, () =>
        {
            _edges.DeleteAll();
            _endpointDocs.DeleteAll();
            _relationDocs.DeleteAll();
            _tombstones.DeleteAll();
            _endpoints.Clear();
            _typeById.Clear();
            foreach (var key in _graph.GetAllNodeKeys().ToList())
                _graph.RemoveNode(key);
            return Task.CompletedTask;
        });
    }

    // ---- 墓碑（删除意图）与声明原语（先墓碑后物理删除；声明存在即生效）----

    /// <summary>墓碑是否存在。</summary>
    public Task<bool> IsTombstonedAsync(string tombstoneKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tombstoneKey)) return Task.FromResult(false);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadLocked(ct, () => TombstoneExistsCore(tombstoneKey)));
    }

    /// <summary>写墓碑（幂等；同键覆盖）。</summary>
    public Task AddTombstoneAsync(string tombstoneKey, GraphTombstoneDoc doc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tombstoneKey);
        ArgumentNullException.ThrowIfNull(doc);
        return MutateAsync(ct, () => { AddTombstoneCore(tombstoneKey, doc); return Task.CompletedTask; });
    }

    /// <summary>清除单个墓碑；返回是否命中。</summary>
    public Task<bool> RemoveTombstoneAsync(string tombstoneKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tombstoneKey)) return Task.FromResult(false);
        return MutateAsync(ct, () => Task.FromResult(RemoveTombstoneCore(tombstoneKey)));
    }

    /// <summary>列举墓碑（kind 为空 ⇒ 全部）。</summary>
    public Task<IReadOnlyList<GraphTombstoneDoc>> ListTombstonesAsync(string? kind = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GraphTombstoneDoc>>(ReadLocked(ct, () =>
        {
            var result = new List<GraphTombstoneDoc>();
            foreach (var doc in _tombstones.FindAll().OrderBy(d => d["_id"].AsString, StringComparer.Ordinal))
            {
                var t = ReadTombstoneDoc(doc);
                if (t is null) continue;
                if (!string.IsNullOrWhiteSpace(kind) && !string.Equals(t.Kind, kind, StringComparison.Ordinal)) continue;
                result.Add(t);
            }
            return result;
        }));
    }

    /// <summary>清除墓碑（kind 为空 ⇒ 全部）；返回清除条数。</summary>
    public Task<int> ClearTombstonesAsync(string? kind = null, CancellationToken ct = default)
        => MutateAsync(ct, () =>
        {
            var keys = _tombstones.FindAll()
                .Where(d => string.IsNullOrWhiteSpace(kind)
                    || string.Equals(d["Kind"].IsString ? d["Kind"].AsString : null, kind, StringComparison.Ordinal))
                .Select(d => d["_id"].AsString)
                .ToList();
            foreach (var key in keys) _tombstones.Delete(key);
            return Task.FromResult(keys.Count);
        });

    /// <summary>seed 专用：只补缺失，不覆盖既有、不因墓碑写回。<paramref name="nodeKey"/> = _id（键文法单源）。</summary>
    public Task<RelationDoc?> SeedRelationDeclarationAsync(string nodeKey, RelationDoc doc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ArgumentNullException.ThrowIfNull(doc);
        ct.ThrowIfCancellationRequested();
        return MutateAsync(ct, () =>
        {
            if (TombstoneExistsCore(GraphIdentityKeys.DeclarationTombstoneKey(doc)))
                return Task.FromResult<RelationDoc?>(null);
            var existing = ReadRelationDocCore(nodeKey);
            if (existing is not null) return Task.FromResult<RelationDoc?>(existing);
            InsertRelationDocCore(nodeKey, doc);
            return Task.FromResult<RelationDoc?>(doc);
        });
    }

    /// <summary>显式声明：清除声明/边/端点墓碑 → upsert 声明 → 建声明边。</summary>
    public Task DeclareRelationAsync(string nodeKey, RelationDoc doc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ArgumentNullException.ThrowIfNull(doc);
        return MutateAsync(ct, () =>
        {
            RemoveTombstoneCore(GraphIdentityKeys.DeclarationTombstoneKey(doc));
            RemoveTombstoneCore(GraphIdentityKeys.EdgeTombstoneKey(SourceKeyOf(doc), TargetKeyOf(doc), doc.RefType));
            RemoveTombstoneCore(GraphIdentityKeys.EndpointTombstoneKey(doc.SourceType, doc.Source));
            RemoveTombstoneCore(GraphIdentityKeys.EndpointTombstoneKey(doc.TargetType, doc.Target));
            InsertRelationDocCore(nodeKey, doc);
            AddDeclaredEdgeCore(doc);
            return Task.CompletedTask;
        });
    }

    /// <summary>补建声明派生边（Origin=declared；只由声明路径调用）。</summary>
    public Task AddDeclaredEdgeAsync(RelationDoc doc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(doc);
        return MutateAsync(ct, () => { AddDeclaredEdgeCore(doc); return Task.CompletedTask; });
    }

    /// <summary>取边文档（按 _id）；不存在 ⇒ null。</summary>
    public Task<EdgeDoc?> GetEdgeDocAsync(string edgeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(edgeId)) return Task.FromResult<EdgeDoc?>(null);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadLocked(ct, () => ReadEdgeDocCore(edgeId)));
    }

    /// <summary>取端点文档（按唯一键）；不存在 ⇒ null。</summary>
    public Task<EndpointDoc?> GetEndpointDocAsync(string uniqueKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uniqueKey)) return Task.FromResult<EndpointDoc?>(null);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadLocked(ct, () =>
        {
            var doc = _endpointDocs.FindById(uniqueKey);
            return doc is null ? null : ReadEndpointDoc(doc);
        }));
    }

    /// <summary>物理删除声明（不写墓碑；对账/收敛路径专用）。返回是否命中。</summary>
    public Task<bool> DeleteRelationDeclarationAsync(string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        return MutateAsync(ct, () =>
        {
            var doc = ReadRelationDocCore(nodeKey);
            if (doc is null) return Task.FromResult(false);
            RemoveEdgeCore(BuildEdgeId(SourceKeyOf(doc), TargetKeyOf(doc), doc.RefType));
            RemoveRelationDocCore(nodeKey);
            return Task.FromResult(true);
        });
    }

    /// <summary>删除声明 + 墓碑 + 派生边（单一原子语义入口）。</summary>
    public Task<GraphDeleteOutcome> DeleteDeclarationDurablyAsync(string nodeKey, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        return MutateAsync(ct, () =>
        {
            var doc = ReadRelationDocCore(nodeKey);
            if (doc is null)
                return Task.FromResult(new GraphDeleteOutcome(nodeKey, GraphDeleteStatus.NotFound, "声明不存在"));

            var tombstoneKey = GraphIdentityKeys.DeclarationTombstoneKey(doc);
            AddTombstoneCore(tombstoneKey, NewTombstone(GraphTombstoneKinds.Declaration, nodeKey, reason, doc));
            RemoveEdgeCore(BuildEdgeId(SourceKeyOf(doc), TargetKeyOf(doc), doc.RefType));
            RemoveRelationDocCore(nodeKey);
            return Task.FromResult(new GraphDeleteOutcome(nodeKey, GraphDeleteStatus.Ok,
                "声明已物理删除，墓碑已写（重启/装配/重建均不恢复）", tombstoneKey));
        });
    }


    /// <summary>删除边 + 匹配声明 + 墓碑（边键不含端口 ⇒ 批量删除该端点的端口变体声明）。</summary>
    public Task<GraphDeleteOutcome> DeleteEdgeDurablyAsync(string edgeId, string reason, CancellationToken ct = default)
    {
        if (!EdgeIdKeys.TryParse(edgeId, out var sourceKey, out var targetKey, out var refType))
            return Task.FromResult(new GraphDeleteOutcome(edgeId ?? string.Empty, GraphDeleteStatus.Rejected,
                "边键不符合 {src}::{tgt}::{refType} 文法"));

        return MutateAsync(ct, () =>
        {
            var edge = ReadEdgeDocCore(edgeId);
            var matches = ReadRelationDocsCore(null)
                .Where(d => string.Equals(SourceKeyOf(d), sourceKey, StringComparison.Ordinal)
                    && string.Equals(TargetKeyOf(d), targetKey, StringComparison.Ordinal)
                    && string.Equals(d.RefType, refType, StringComparison.Ordinal))
                .ToList();
            var tombstoneKey = GraphIdentityKeys.EdgeTombstoneKey(sourceKey, targetKey, refType);
            if (edge is null && matches.Count == 0)
                return Task.FromResult(new GraphDeleteOutcome(edgeId, GraphDeleteStatus.NotFound, "引用边不存在"));

            AddTombstoneCore(tombstoneKey, NewTombstone(GraphTombstoneKinds.Edge, edgeId, reason));
            foreach (var d in matches) DeleteDeclarationCore(d, reason);
            RemoveEdgeCore(edgeId);
            return Task.FromResult(new GraphDeleteOutcome(edgeId, GraphDeleteStatus.Ok,
                $"引用边已物理删除（关联声明 {matches.Count} 条同删，墓碑已写）", tombstoneKey));
        });
    }

    /// <summary>删除端点 + incident 声明/边 + 墓碑（绝不触碰 SQLite 实体）。</summary>
    public Task<GraphDeleteOutcome> DeleteEndpointDurablyAsync(ResourceIdentifier id, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var key = id.GetUniqueKey();
        return MutateAsync(ct, () =>
        {
            var endpoint = _endpointDocs.FindById(key);
            var affected = ReadRelationDocsCore(null).Where(d => ReferencesResource(d, id)).ToList();
            var tombstoneKey = GraphIdentityKeys.EndpointTombstoneKey(id);
            if (endpoint is null && affected.Count == 0)
                return Task.FromResult(new GraphDeleteOutcome(key, GraphDeleteStatus.NotFound, $"端点不存在：{key}"));

            AddTombstoneCore(tombstoneKey, NewTombstone(GraphTombstoneKinds.Endpoint, key, reason));
            foreach (var d in affected) DeleteDeclarationCore(d, reason);

            var edgeIds = new List<string>();
            edgeIds.AddRange(_edges.Find(Query.EQ("SourceId", key)).Select(d => d["_id"].AsString));
            edgeIds.AddRange(_edges.Find(Query.EQ("TargetId", key)).Select(d => d["_id"].AsString));
            edgeIds.AddRange(_edges.FindAll()
                .Where(d => d["_id"].IsString && d["_id"].AsString.StartsWith(key + "::", StringComparison.Ordinal))
                .Select(d => d["_id"].AsString));
            foreach (var edgeId in edgeIds.Distinct(StringComparer.Ordinal).ToList()) RemoveEdgeCore(edgeId);

            _endpoints.Remove(key);
            _typeById.Remove(KeyOfType(id));
            _endpointDocs.Delete(key);
            if (_graph.ContainsNode(key)) _graph.RemoveNode(key);
            return Task.FromResult(new GraphDeleteOutcome(key, GraphDeleteStatus.Ok,
                $"端点已物理删除（关联声明 {affected.Count} 条；墓碑已写）", tombstoneKey));
        });
    }

    /// <summary>取单条声明（按 _id）；不存在 ⇒ null。</summary>
    public Task<RelationDoc?> GetRelationDeclarationAsync(string nodeKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ReadLocked(ct, () =>
        {
            var doc = _relationDocs.FindById(nodeKey);
            return doc is null ? null : ReadRelationDoc(doc);
        }));
    }

    /// <summary>列举声明（<paramref name="workflowId"/> 为空则全部）。</summary>
    public Task<IReadOnlyList<RelationDoc>> GetRelationDeclarationsAsync(string? workflowId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RelationDoc>>(ReadLocked(ct, () => ReadRelationDocsCore(workflowId)));
    }

    /// <summary>由声明投影的关系边集（声明存在即生效）。</summary>
    public IReadOnlyList<NodeRelation> GetRelationEdges(string workflowId)
    {
        var docs = ReadLocked(CancellationToken.None, () => ReadRelationDocsCore(workflowId));
        var edges = new List<NodeRelation>();
        foreach (var d in docs)
        {
            var edge = NodeRelationDeriver.DeriveEdgeFromDeclaration(d.WorkflowId, ToDeclaration(d));
            if (edge is not null) edges.Add(edge);
        }
        return edges;
    }

    /// <summary>RelationDoc → 领域声明（供 DeriveEdgeFromDeclaration 消费）。</summary>
    internal static RelationDeclaration ToDeclaration(RelationDoc d) => new()
    {
        Source = new RelationEndpoint { ResourceType = string.IsNullOrWhiteSpace(d.SourceType) ? NodeResourceTypes.Node : d.SourceType, Id = d.Source, Port = d.SourcePort },
        Target = new RelationEndpoint { ResourceType = string.IsNullOrWhiteSpace(d.TargetType) ? NodeResourceTypes.Node : d.TargetType, Id = d.Target, Port = d.TargetPort },
        RefType = string.IsNullOrWhiteSpace(d.RefType) ? ReferenceTypes.NodeToNode : d.RefType,
        KeyMappingRef = d.KeyMappingRef,
    };

    /// <summary>读面核心（调用方须已持 <see cref="_gate"/>）。</summary>
    private List<RelationDoc> ReadRelationDocsCore(string? workflowId)
    {
        var result = new List<RelationDoc>();
        foreach (var doc in _relationDocs.FindAll())
        {
            var r = ReadRelationDoc(doc);
            if (r is null) continue;
            if (!string.IsNullOrWhiteSpace(workflowId) && !string.Equals(r.WorkflowId, workflowId, StringComparison.Ordinal)) continue;
            result.Add(r);
        }
        return result;
    }

    private RelationDoc? ReadRelationDoc(BsonDocument doc)
    {
        var text = DocText(doc);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return _codec.TryDeserializeRecord(text, out _, out _, out _, out var body) ? RelationFromRecord(body) : null;
    }

    /// <summary>RelationDoc → USharp 记录体（字段名 camelCase；写/读同源）。</summary>
    private static UsharpRecord RelationRecord(RelationDoc d) => new(new[]
    {
        new KeyValuePair<string, UsharpValue>("relationId", UsharpScalar.Str(d.RelationId)),
        new KeyValuePair<string, UsharpValue>("workflowId", UsharpScalar.Str(d.WorkflowId)),
        new KeyValuePair<string, UsharpValue>("nodeKey", UsharpScalar.Str(d.NodeKey)),
        new KeyValuePair<string, UsharpValue>("source", UsharpScalar.Str(d.Source)),
        new KeyValuePair<string, UsharpValue>("sourceType", UsharpScalar.Str(d.SourceType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("sourcePort", UsharpScalar.Str(d.SourcePort ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("target", UsharpScalar.Str(d.Target)),
        new KeyValuePair<string, UsharpValue>("targetType", UsharpScalar.Str(d.TargetType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("targetPort", UsharpScalar.Str(d.TargetPort ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("refType", UsharpScalar.Str(d.RefType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("keyMappingRef", UsharpScalar.Str(d.KeyMappingRef ?? string.Empty)),
    });

    private static RelationDoc RelationFromRecord(UsharpRecord body) => new()
    {
        RelationId = S(body, "relationId") ?? string.Empty,
        WorkflowId = S(body, "workflowId") ?? string.Empty,
        NodeKey = S(body, "nodeKey") ?? string.Empty,
        Source = S(body, "source") ?? string.Empty,
        SourceType = S(body, "sourceType") ?? string.Empty,
        SourcePort = S(body, "sourcePort"),
        Target = S(body, "target") ?? string.Empty,
        TargetType = S(body, "targetType") ?? string.Empty,
        TargetPort = S(body, "targetPort"),
        RefType = S(body, "refType") ?? string.Empty,
        KeyMappingRef = S(body, "keyMappingRef"),
    };

    // ---- 唯一读面（节点 / 边 / 声明三面分离；墓碑集合不可见）----

    /// <summary>只读枚举 LiteDB 集合名（墓碑集合不出现在业务读面）。</summary>
    public IReadOnlyList<string> ListCollections()
        => _db.GetCollectionNames()
            .Where(n => !string.Equals(n, _tombstones.Name, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>端点分页（Total = 集合计数；_id 升序稳定）。</summary>
    public GraphPage<GraphEndpointRow> ReadEndpoints(int offset, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (off, take) = NormalizePage(offset, limit);
        return ReadLocked(ct, () =>
        {
            var total = _endpointDocs.Count();
            var items = new List<GraphEndpointRow>();
            foreach (var d in _endpointDocs.FindAll()
                .OrderBy(x => x["_id"].AsString, StringComparer.Ordinal).Skip(off).Take(take))
            {
                var key = d["_id"].IsString ? d["_id"].AsString : string.Empty;
                var e = ReadEndpointDoc(d);
                var id = new ResourceIdentifier { ResourceType = e?.ResourceType ?? string.Empty, Id = e?.Id ?? string.Empty };
                var refs = _graph.ContainsNode(key) ? GetReferencesCore(key, includeIncoming: true) : new List<ResourceReference>();
                items.Add(new GraphEndpointRow(key, e?.ResourceType ?? string.Empty, e?.Id ?? string.Empty, e?.Symbol,
                    refs.Count(r => !r.IsIncoming), refs.Count(r => r.IsIncoming), e?.UpdatedAt));
            }
            return new GraphPage<GraphEndpointRow>(items, total, off, take);
        });
    }

    /// <summary>边分页（Total = 集合计数；_id 升序稳定）。</summary>
    public GraphPage<GraphEdgeRow> ReadEdges(int offset, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (off, take) = NormalizePage(offset, limit);
        return ReadLocked(ct, () =>
        {
            var total = _edges.Count();
            var items = new List<GraphEdgeRow>();
            foreach (var d in _edges.FindAll()
                .OrderBy(x => x["_id"].AsString, StringComparer.Ordinal).Skip(off).Take(take))
            {
                var key = d["_id"].IsString ? d["_id"].AsString : string.Empty;
                var e = ReadEdgeDoc(d);
                if (e is null) continue;
                items.Add(new GraphEdgeRow(key, e.SourceId, e.TargetId, e.RefType, e.Strength, e.Origin, null));
            }
            return new GraphPage<GraphEdgeRow>(items, total, off, take);
        });
    }

    /// <summary>声明分页（Total = 集合计数；_id 升序稳定；Tombstoned 恒 false——墓碑即已物理删除）。</summary>
    public GraphPage<GraphDeclarationRow> ReadDeclarations(int offset, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (off, take) = NormalizePage(offset, limit);
        return ReadLocked(ct, () =>
        {
            var total = _relationDocs.Count();
            var items = new List<GraphDeclarationRow>();
            foreach (var d in _relationDocs.FindAll()
                .OrderBy(x => x["_id"].AsString, StringComparer.Ordinal).Skip(off).Take(take))
            {
                var key = d["_id"].IsString ? d["_id"].AsString : string.Empty;
                var r = ReadRelationDoc(d);
                if (r is null) continue;
                items.Add(new GraphDeclarationRow(key, r.RelationId, r.WorkflowId,
                    $"{r.SourceType}:{r.Source}", $"{r.TargetType}:{r.Target}", r.RefType, false));
            }
            return new GraphPage<GraphDeclarationRow>(items, total, off, take);
        });
    }

    /// <summary>分页参数归一（offset ≥ 0；limit 夹取 1..1000）。</summary>
    private static (int Offset, int Limit) NormalizePage(int offset, int limit)
        => (Math.Max(0, offset), Math.Max(1, Math.Min(limit, 1000)));

    // ---- 图算法（邻接/路径/依赖，供诊断与影响链查询）----

    public IReadOnlyList<string> GetNeighbors(string key) => ReadLocked(CancellationToken.None, () => _graph.GetNeighbors(key));

    /// <summary>全部路径（**单一实现** = <c>DefaultPathFinder&lt;string&gt;.FindAllPaths</c>）；邻接 = owner 内存图出边投影，
    /// 惰性求值 ⇒ 遍历仍在本类读门（<see cref="ReadLocked{T}"/>）内。与委托前私有 DFS 的逐字等价口径：
    /// ① 路径节点数上界 = <paramref name="maxDepth"/>；② 起点 = 终点时旧实现产出 0 条（内核产出单点解 [起点]，
    /// 此处按 <c>Count &gt;= 2</c> 滤除）；③ 取消检查粒度 = 每节点展开一次（旧为每邻居；预取消恒抛）。</summary>
    public Task<IReadOnlyList<string[]>> FindPathsAsync(string fromKey, string toKey, int maxDepth = 5, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string[]>>(ReadLocked(ct, () =>
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> Adjacency(string node)
            {
                ct.ThrowIfCancellationRequested();
                return OutgoingTargets(node);
            }

            var paths = new DefaultPathFinder<string>().FindAllPaths(fromKey, toKey, Adjacency, maxDepth);
            return (IReadOnlyList<string[]>)paths.Where(p => p.Count >= 2).Select(p => p.ToArray()).ToList();
        }));

    /// <summary>依赖集（**单一实现** = <c>DefaultPathFinder&lt;string&gt;.GetReachableNodes</c>）；语义 = 「经 ≥1 条边可达」
    /// （委托前口径：起点仅在环上回达自身时计入）—— 经**哨兵根**起跳保真（内核「含自身」约定落在哨兵上，而非起点）。
    /// 结果按 Ordinal 排序（对齐 <see cref="ReferenceGraphQuery.GetImpactDomainAsync"/>；委托前为 HashSet 不定序）。</summary>
    public Task<IReadOnlyList<string>> GetDependenciesAsync(string key, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(ReadLocked(ct, () =>
        {
            ct.ThrowIfCancellationRequested();
            var root = SentinelRootNotInGraph();
            IEnumerable<string> Adjacency(string node)
            {
                ct.ThrowIfCancellationRequested();
                return node == root ? OutgoingTargets(key) : OutgoingTargets(node);
            }

            var reachable = new DefaultPathFinder<string>().GetReachableNodes(root, Adjacency);
            return (IReadOnlyList<string>)reachable
                .Where(k => !string.Equals(k, root, StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
        }));

    /// <summary>内核算法消费的出边邻接投影（单一实现；调用方须已持 <see cref="_gate"/>，即仅在 <see cref="ReadLocked{T}"/> 内使用）。</summary>
    private IEnumerable<string> OutgoingTargets(string node)
        => _graph.GetOutgoingEdges(node).Select(e => e.Target);

    /// <summary>可达集遍历的虚拟起点（哨兵根）：前缀 NUL 与真实端点键（<c>{type}:{id}</c>）无交集；仍以
    /// <c>ICoreGraph.ContainsNode</c> 兜住理论碰撞（图有限 ⇒ 循环必终止，故哨兵必与图内节点不同）。</summary>
    private string SentinelRootNotInGraph()
    {
        var root = SentinelRoot;
        while (_graph.ContainsNode(root)) root += "~";
        return root;
    }

    private const string SentinelRoot = "\u0000refgraph-root";


    // ---- IAsyncDisposable ----

    /// <summary>是否已释放（供单实例注册表判断是否需要重建；与 <see cref="Dispose"/> 的幂等守护共用同一标记）。</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _graph.Dispose(); } catch { }
        try { _db.Dispose(); } catch { }
        try { _gate.Dispose(); } catch { }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    // ---- 内部 ----

    // 图读写门有界等待（C3 加固）：门一旦被长持/异常泄漏，无超时等待会无限阻塞调用线程，
    // 拖死整条 PersistAsync（OnAfterStep）→ 任务永久 Running。加有界等待，超时抛异常转非致命归类。
    private static readonly TimeSpan GateLeaseTimeout = TimeSpan.FromSeconds(5);

    private async Task MutateAsync(CancellationToken ct, Func<Task> body, bool affectsMembership = false)
    {
        if (!await _gate.WaitAsync(GateLeaseTimeout, ct).ConfigureAwait(false))
            throw new TimeoutException($"LiteDB 图写门等待超时（{GateLeaseTimeout.TotalSeconds}s），疑似写门被长持未释放。");
        try
        {
            await body().ConfigureAwait(false);
            Interlocked.Increment(ref _version);
            if (affectsMembership) Interlocked.Increment(ref _membershipVersion);
        }
        finally { _gate.Release(); }
    }

    private async Task<T> MutateAsync<T>(CancellationToken ct, Func<Task<T>> body, bool affectsMembership = false)
    {
        if (!await _gate.WaitAsync(GateLeaseTimeout, ct).ConfigureAwait(false))
            throw new TimeoutException($"LiteDB 图写门等待超时（{GateLeaseTimeout.TotalSeconds}s），疑似写门被长持未释放。");
        try
        {
            var result = await body().ConfigureAwait(false);
            Interlocked.Increment(ref _version);
            if (affectsMembership) Interlocked.Increment(ref _membershipVersion);
            return result;
        }
        finally { _gate.Release(); }
    }

    /// <summary>该边类型是否影响树成员（唯一判据：新增成员边类型只改这里）。</summary>
    private static bool AffectsMembership(string refType)
        => string.Equals(refType, CrossDbReferenceTypes.HistoryOwnsTask, StringComparison.Ordinal);

    /// <summary>读面串行化（B5）：枚举内存图邻接的读方法经本助手与写面（<see cref="_gate"/>）互斥，
    /// 避免并发写图时读到不一致中间态或触发集合变更异常。调用方须为最外层读方法——不可在已持 <see cref="_gate"/> 的上下文内再调本助手。</summary>
    private T ReadLocked<T>(CancellationToken ct, Func<T> read)
    {
        if (!_gate.Wait(GateLeaseTimeout))
            throw new TimeoutException($"LiteDB 图读门等待超时（{GateLeaseTimeout.TotalSeconds}s），疑似写门被长持未释放。");
        try { return read(); }
        finally { _gate.Release(); }
    }

    private static string BuildEdgeId(string sourceKey, string targetKey, string refType)
        => EdgeIdKeys.Build(sourceKey, targetKey, refType);

    private bool TombstoneExistsCore(string tombstoneKey)
        => !string.IsNullOrWhiteSpace(tombstoneKey) && _tombstones.FindById(tombstoneKey) is not null;

    private void AddTombstoneCore(string tombstoneKey, GraphTombstoneDoc doc)
        => _tombstones.Upsert(new BsonDocument
        {
            ["_id"] = tombstoneKey,
            ["Kind"] = doc.Kind ?? string.Empty,
            ["Doc"] = _codec.SerializeRecord("tombstone:" + tombstoneKey, "Tombstones", tombstoneKey, TombstoneRecord(doc)),
        });

    private bool RemoveTombstoneCore(string tombstoneKey)
    {
        if (string.IsNullOrWhiteSpace(tombstoneKey) || _tombstones.FindById(tombstoneKey) is null) return false;
        _tombstones.Delete(tombstoneKey);
        return true;
    }

    private static GraphTombstoneDoc NewTombstone(string kind, string targetKey, string reason, RelationDoc? doc = null) => new()
    {
        Kind = kind,
        TargetKey = targetKey ?? string.Empty,
        WorkflowId = doc?.WorkflowId,
        SourceType = doc?.SourceType,
        Source = doc?.Source,
        SourcePort = doc?.SourcePort,
        TargetType = doc?.TargetType,
        Target = doc?.Target,
        TargetPort = doc?.TargetPort,
        RefType = doc?.RefType,
        KeyMappingRef = doc?.KeyMappingRef,
        CreatedAt = DateTime.UtcNow.ToString("O"),
        Reason = reason,
    };

    private static UsharpRecord TombstoneRecord(GraphTombstoneDoc t) => new(new[]
    {
        new KeyValuePair<string, UsharpValue>("kind", UsharpScalar.Str(t.Kind ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("targetKey", UsharpScalar.Str(t.TargetKey ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("workflowId", UsharpScalar.Str(t.WorkflowId ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("sourceType", UsharpScalar.Str(t.SourceType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("source", UsharpScalar.Str(t.Source ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("sourcePort", UsharpScalar.Str(t.SourcePort ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("targetType", UsharpScalar.Str(t.TargetType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("target", UsharpScalar.Str(t.Target ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("targetPort", UsharpScalar.Str(t.TargetPort ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("refType", UsharpScalar.Str(t.RefType ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("keyMappingRef", UsharpScalar.Str(t.KeyMappingRef ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("createdAt", UsharpScalar.Str(t.CreatedAt ?? string.Empty)),
        new KeyValuePair<string, UsharpValue>("reason", UsharpScalar.Str(t.Reason ?? string.Empty)),
    });

    private GraphTombstoneDoc? ReadTombstoneDoc(BsonDocument doc)
    {
        var text = DocText(doc);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!_codec.TryDeserializeRecord(text!, out _, out _, out _, out var body)) return null;
        return new GraphTombstoneDoc
        {
            Kind = S(body, "kind") ?? string.Empty,
            TargetKey = S(body, "targetKey") ?? string.Empty,
            WorkflowId = S(body, "workflowId"),
            SourceType = S(body, "sourceType"),
            Source = S(body, "source"),
            SourcePort = S(body, "sourcePort"),
            TargetType = S(body, "targetType"),
            Target = S(body, "target"),
            TargetPort = S(body, "targetPort"),
            RefType = S(body, "refType"),
            KeyMappingRef = S(body, "keyMappingRef"),
            CreatedAt = S(body, "createdAt") ?? string.Empty,
            Reason = S(body, "reason"),
        };
    }


    // ---- 门内核原语（调用方须已持 _gate；公开方法之间禁止互调，避免不可重入门自死锁）----

    private static string SourceTypeOf(RelationDoc d)
        => string.IsNullOrWhiteSpace(d.SourceType) ? NodeResourceTypes.Node : d.SourceType;

    private static string TargetTypeOf(RelationDoc d)
        => string.IsNullOrWhiteSpace(d.TargetType) ? NodeResourceTypes.Node : d.TargetType;

    private static string SourceKeyOf(RelationDoc d) => SourceTypeOf(d) + ResourceIdentifier.UniqueKeySeparator + d.Source;

    private static string TargetKeyOf(RelationDoc d) => TargetTypeOf(d) + ResourceIdentifier.UniqueKeySeparator + d.Target;

    private void InsertRelationDocCore(string nodeKey, RelationDoc doc)
    {
        _relationDocs.DeleteMany(Query.EQ("_id", nodeKey));
        _relationDocs.Insert(new BsonDocument
        {
            ["_id"] = nodeKey,
            ["RelationId"] = doc.RelationId ?? string.Empty,
            ["WorkflowId"] = doc.WorkflowId ?? string.Empty,
            ["Doc"] = _codec.SerializeRecord("relation:" + nodeKey, "Relations", nodeKey, RelationRecord(doc)),
        });
    }

    private RelationDoc? ReadRelationDocCore(string nodeKey)
    {
        var doc = _relationDocs.FindById(nodeKey);
        return doc is null ? null : ReadRelationDoc(doc);
    }

    private EdgeDoc? ReadEdgeDocCore(string edgeId)
    {
        var doc = _edges.FindById(edgeId);
        return doc is null ? null : ReadEdgeDoc(doc);
    }

    /// <summary>声明派生边（Origin=declared；唯一写在声明路径内）。</summary>
    private void AddDeclaredEdgeCore(RelationDoc doc)
    {
        var src = new ResourceIdentifier { ResourceType = SourceTypeOf(doc), Id = doc.Source };
        var tgt = new ResourceIdentifier { ResourceType = TargetTypeOf(doc), Id = doc.Target };
        var sourceKey = src.GetUniqueKey();
        var targetKey = tgt.GetUniqueKey();
        var edgeId = BuildEdgeId(sourceKey, targetKey, doc.RefType);
        EnsureNodeInMemory(sourceKey);
        EnsureNodeInMemory(targetKey);
        _edges.DeleteMany(Query.EQ("_id", edgeId));
        _edges.Insert(ToEdgeDoc(sourceKey, src, targetKey, tgt, doc.RefType, 1.0, EdgeOrigin.Declared));
        if (!_graph.ContainsEdge(edgeId))
            _graph.AddEdge(edgeId, sourceKey, targetKey, isDirected: true, new ReferenceEdgeData(doc.RefType, 1.0), 1.0);
    }

    private void RemoveEdgeCore(string edgeId)
    {
        _edges.DeleteMany(Query.EQ("_id", edgeId));
        if (_graph.ContainsEdge(edgeId)) _graph.RemoveEdge(edgeId);
    }

    private void RemoveRelationDocCore(string nodeKey) => _relationDocs.DeleteMany(Query.EQ("_id", nodeKey));

    /// <summary>删除声明（墓碑 + 派生边 + 文档）；调用方须已持门。</summary>
    private void DeleteDeclarationCore(RelationDoc doc, string reason)
    {
        AddTombstoneCore(GraphIdentityKeys.DeclarationTombstoneKey(doc),
            NewTombstone(GraphTombstoneKinds.Declaration, doc.NodeKey, reason, doc));
        RemoveEdgeCore(BuildEdgeId(SourceKeyOf(doc), TargetKeyOf(doc), doc.RefType));
        RemoveRelationDocCore(doc.NodeKey);
    }

    private void EnsureNodeInMemory(string key)
    {
        if (_graph.ContainsNode(key)) return;
        _graph.AddNode(key, _endpoints.TryGetValue(key, out var r) ? r : new Resource { Identifier = KeyToIdentifier(key) });
    }

    private static string KeyOfType(ResourceIdentifier id)
        => (id.ResourceType ?? string.Empty) + ResourceIdentifier.UniqueKeySeparator + (id.Id ?? string.Empty);

    /// <summary>refType → 节点关系类别（D3 收口：解析唯一实现下沉 <see cref="NodeRelationRefCodec.ParseType"/>，本处只消费）。</summary>
    private static NodeRelationType? ParseNodeRelationType(string refType)
        => NodeRelationRefCodec.ParseType(refType);

    internal static ResourceIdentifier KeyToIdentifier(string key)
    {
        var i = key.IndexOf(':');
        return i < 0 ? new ResourceIdentifier { Id = key } : new ResourceIdentifier { ResourceType = key.Substring(0, i), Id = key.Substring(i + 1) };
    }

    private static Resource ToResource(ResourceIdentifier id, IReadOnlyDictionary<string, object>? metadata, string? symbol)
        => new()
        {
            Identifier = id,
            Symbol = symbol ?? id.Id,
            Metadata = metadata is null ? new Dictionary<string, object>() : new Dictionary<string, object>(metadata),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
        };

    /// <summary>持久化端点符号/metadata 到 ur_endpoints（可重建缓存，权威仍在 SQL）；{ _id, Doc } 形态，Doc = USharp [Record] 实例。</summary>
    private void PersistEndpointDoc(string key, Resource resource, ResourceIdentifier id)
    {
        var doc = new BsonDocument
        {
            ["_id"] = key,
            ["ResourceType"] = id.ResourceType ?? string.Empty,
            ["Id"] = id.Id ?? string.Empty,
            ["Doc"] = _codec.SerializeRecord("endpoint:" + key, "Endpoints", key, EndpointRecord(key, resource, id)),
        };
        _endpointDocs.Upsert(doc);
    }

    // ---- 跨库写单元 Outbox（IOutboxStore；与边同库、同单写门）----

    /// <inheritdoc />
    public async Task UpsertPendingAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.IdempotencyKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _outbox.Upsert(new BsonDocument
            {
                ["_id"] = entry.IdempotencyKey,
                ["UnitName"] = entry.UnitName ?? string.Empty,
                ["PayloadJson"] = entry.PayloadJson ?? string.Empty,
                ["Attempts"] = entry.Attempts,
                ["LastError"] = entry.LastError ?? string.Empty,
                ["State"] = string.IsNullOrWhiteSpace(entry.State) ? OutboxStates.Pending : entry.State,
                ["FirstFailedAt"] = string.IsNullOrWhiteSpace(entry.FirstFailedAt)
                    ? DateTimeOffset.UtcNow.ToString("o")
                    : entry.FirstFailedAt,
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxEntry>> ListPendingAsync(int limit = 100, CancellationToken ct = default)
    {
        if (limit <= 0) return Array.Empty<OutboxEntry>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var docs = _outbox.Find(Query.EQ("State", OutboxStates.Pending))
                .OrderBy(d => d["FirstFailedAt"].AsString)
                .Take(Math.Max(1, limit))
                .ToList();
            var result = new List<OutboxEntry>(docs.Count);
            foreach (var d in docs)
                result.Add(ToOutboxEntry(d));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkAppliedAsync(string idempotencyKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        await SetOutboxStateAsync(idempotencyKey, OutboxStates.Applied, error: null, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAbandonedAsync(string idempotencyKey, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        await SetOutboxStateAsync(idempotencyKey, OutboxStates.Abandoned, reason, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(int Pending, int Abandoned)> CountAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = _outbox.Count(Query.EQ("State", OutboxStates.Pending));
            var abandoned = _outbox.Count(Query.EQ("State", OutboxStates.Abandoned));
            return ((int)pending, (int)abandoned);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SetOutboxStateAsync(string key, string state, string? error, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var doc = _outbox.FindById(key);
            if (doc is null) return;
            doc["State"] = state;
            if (error is not null) doc["LastError"] = error;
            _outbox.Update(doc);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static OutboxEntry ToOutboxEntry(BsonDocument d) => new(
        d["_id"].AsString,
        d["UnitName"].AsString,
        d["PayloadJson"].AsString,
        d.ContainsKey("Attempts") ? d["Attempts"].AsInt32 : 0,
        d.ContainsKey("LastError") && !string.IsNullOrEmpty(d["LastError"].AsString) ? d["LastError"].AsString : null,
        d.ContainsKey("State") ? d["State"].AsString : OutboxStates.Pending,
        d.ContainsKey("FirstFailedAt") ? d["FirstFailedAt"].AsString : string.Empty);

    // ---- 边文档编解码（ur_references）----

    private BsonDocument ToEdgeDoc(string sourceKey, ResourceIdentifier src, string targetKey, ResourceIdentifier tgt,
        string refType, double strength, string origin = EdgeOrigin.Runtime)
    {
        var id = BuildEdgeId(sourceKey, targetKey, refType);
        return new BsonDocument
        {
            ["_id"] = id,
            ["SourceId"] = sourceKey,
            ["TargetId"] = targetKey,
            ["RefType"] = refType,
            ["Origin"] = origin ?? EdgeOrigin.Runtime,
            ["Doc"] = _codec.SerializeRecord("edge:" + id, "References", id, EdgeRecord(new EdgeDoc
            {
                SourceId = sourceKey,
                SourceType = src.ResourceType,
                TargetId = targetKey,
                TargetType = tgt.ResourceType,
                RefType = refType,
                Strength = strength,
                Origin = origin ?? EdgeOrigin.Runtime,
            })),
        };
    }
}

