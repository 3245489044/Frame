using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using LiteDB;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Configuration;
using Operation.DataStoreDriven.ReferenceSchema;

using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.StorageFramework.Core;

namespace Operation.DataStoreDriven.Graph;

/// <summary>
/// 引用关系图协调器 —— 唯一读写 LiteDB 引用关系的组件。
/// 设计原则（对齐「LiteDB 的唯一作用 = 维护 SQL 中 task/node 的关系，且数据与 SQL 完全不同」）：
///   · SQL 拥有实体（ur_tasks / ur_nodes 存 task/node 本体与载荷），本协调器**不在 LiteDB 复制实体数据**（无 ur_resources）；
///   · LiteDB 只持久化**引用边**（ur_references 集合，唯一集合）——即 task ↔ node / node ↔ node 的关系，与 SQL 的实体行完全不同；
///   · 端点（taskKey / wf.node）只是关系端点键，其存在性按 SQL 写透时序保证（SQL 先写、本协调器随后同步边）；
///   · 会话内内存维护最小端点注册表（供引用完整性 fail-fast 校验与 UI 展示端点符号名），**不落盘**；
///   · 每次 SQL 变更（task 入队/状态/载荷/删除、node 写/删、引用增删）都经写透面收敛到 LiteDB。
/// 本类同时实现 <see cref="IResourceReferenceStore"/> 与 <see cref="INodeRelationStore"/> 端口，保持既有消费方（编排器/对账器）编译兼容，
/// 但资源方法仅操作内存端点注册表（SQL 是实体真源），引用方法操作 LiteDB 持久边 —— 两者数据面完全不同。
/// </summary>
public sealed class ReferenceGraphCoordinator : IResourceReferenceStore, INodeRelationStore, IAsyncDisposable
{
    private readonly LiteDatabase _db;                                    // 唯一持有句柄
    private readonly ILiteCollection<BsonDocument> _edges;                // ur_references（唯一集合，纯边）
    private readonly ILiteCollection<BsonDocument> _endpointDocs;         // ur_endpoints（端点符号/metadata 落盘缓存；权威在 SQL）
    private readonly ICoreGraph<string, Resource, ReferenceEdgeData> _graph; // 热邻接（内存，由边重建）
    private readonly Dictionary<string, Resource> _endpoints;             // 会话内存端点注册表（由 ur_endpoints + 边重建）
    private readonly Dictionary<string, string> _typeById;                // "{type}:{id}" -> key（端点 O(1) 查找；对齐 GetUniqueKey）
    private readonly SemaphoreSlim _gate = new(1, 1);                     // 单写
    // 词表武装（B3）：写边前校验边类型是否在词表 DSL 声明（未装配 ⇒ 不校验，零回归）。
    private readonly IReferenceTypeRegistry? _vocabulary;
    private readonly ReferenceVocabularyGate _vocabularyGate;
    private readonly IReferenceEdgeDiagnostics _edgeDiagnostics;
    private bool _initialized;
    private int _disposed;

    /// <summary>边文档主键（_id）分隔符（单一权威）。端点键已用 <see cref="ResourceIdentifier.UniqueKeySeparator"/>（":"），
    /// 边键在其上再叠加本分隔符，避免与端点键内的 ":" 冲突；原实现用不可打印控制符 \u0001，重构为可读/可 grep 的 "::"。</summary>
    private const string EdgeKeySeparator = "::";

    private ReferenceGraphCoordinator(
        LiteDatabase db, string edgesCollection, string endpointsCollection,
        IReferenceTypeRegistry? vocabulary = null,
        ReferenceVocabularyGate vocabularyGate = ReferenceVocabularyGate.Report,
        IReferenceEdgeDiagnostics? edgeDiagnostics = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _edges = _db.GetCollection<BsonDocument>(edgesCollection);
        _endpointDocs = _db.GetCollection<BsonDocument>(endpointsCollection);
        _graph = new CoreGraph<string, Resource, ReferenceEdgeData>();
        _endpoints = new Dictionary<string, Resource>(StringComparer.Ordinal);
        _typeById = new Dictionary<string, string>(StringComparer.Ordinal);
        _vocabulary = vocabulary;
        _vocabularyGate = vocabularyGate;
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
        IReferenceEdgeDiagnostics? edgeDiagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
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
            db, prefix + TableNames.References, prefix + TableNames.Endpoints,
            vocabulary, vocabularyGate, edgeDiagnostics);
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
        _endpoints.Clear();
        _typeById.Clear();
        // ① 载入持久端点（ur_endpoints；重启保留符号/metadata，避免造裸资源退化图可读性）。旧数据无该集合 → 空集，走纯边兜底。
        foreach (var doc in _endpointDocs.FindAll())
        {
            var key = doc["_id"].IsString ? doc["_id"].AsString : null;
            if (key is null) continue;
            var resourceType = doc["ResourceType"].IsString ? doc["ResourceType"].AsString : string.Empty;
            var id = doc["Id"].IsString ? doc["Id"].AsString : string.Empty;
            var symbol = doc["Symbol"].IsString ? doc["Symbol"].AsString : null;
            var metadata = ReadMetadata(doc);
            var resource = new Resource
            {
                Identifier = new ResourceIdentifier { ResourceType = resourceType, Id = id },
                Symbol = symbol ?? id,
                Metadata = metadata,
            };
            _endpoints[key] = resource;
            _typeById[KeyOfType(resource.Identifier)] = key;
        }
        // ② 由边重建内存邻接（端点优先用持久符号，缺失则 KeyToIdentifier 造裸资源兜底）。
        foreach (var edge in _edges.FindAll())
        {
            var src = SourceId(edge);
            var tgt = TargetId(edge);
            if (src is null || tgt is null) continue;
            var refType = RefTypeOf(edge);
            if (string.IsNullOrWhiteSpace(refType)) continue;
            EnsureNodeInMemory(src);
            EnsureNodeInMemory(tgt);
            _graph.AddEdge(BuildEdgeId(src, tgt, refType), src, tgt, isDirected: true,
                new ReferenceEdgeData(refType, StrengthOf(edge)), StrengthOf(edge));
        }
        _initialized = true;
    }

    /// <summary>从端点文档读 Metadata（宽松：任意 dict 字段；兼容无 metadata 文档）。</summary>
    private static Dictionary<string, object> ReadMetadata(BsonDocument doc)
    {
        var metadata = new Dictionary<string, object>();
        var bson = doc.TryGetValue("Metadata", out var v) ? v : BsonValue.Null;
        if (bson is not null && !bson.IsNull && bson.IsDocument)
        {
            foreach (var kv in bson.AsDocument)
                if (!kv.Value.IsNull)
                    metadata[kv.Key] = kv.Value.RawValue ?? kv.Value;
        }
        return metadata;
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
            // durable：先删后加（幂等 upsert，避免重复边）
            _edges.DeleteMany(Query.And(
                Query.EQ("SourceId", sourceKey),
                Query.EQ("TargetId", targetKey),
                Query.EQ("RefType", refType)));
            _edges.Insert(ToEdgeDoc(sourceKey, sourceId, targetKey, targetId, refType, strength));
            if (!_graph.ContainsEdge(edgeId))
                _graph.AddEdge(edgeId, sourceKey, targetKey, isDirected: true, new ReferenceEdgeData(refType, strength), strength);
            return Task.CompletedTask;
        });
    }

    public Task RemoveReferenceAsync(ResourceIdentifier sourceId, ResourceIdentifier targetId, string refType,
        CancellationToken ct = default)
    {
        var sourceKey = sourceId.GetUniqueKey();
        var targetKey = targetId.GetUniqueKey();
        return MutateAsync(ct, () =>
        {
            _edges.DeleteMany(Query.And(
                Query.EQ("SourceId", sourceKey),
                Query.EQ("TargetId", targetKey),
                Query.EQ("RefType", refType)));
            var edgeId = BuildEdgeId(sourceKey, targetKey, refType);
            if (_graph.ContainsEdge(edgeId)) _graph.RemoveEdge(edgeId);
            return Task.CompletedTask;
        });
    }

    public Task<IReadOnlyList<ResourceReference>> GetReferencesAsync(ResourceIdentifier resourceId,
        bool includeIncoming = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        var key = resourceId.GetUniqueKey();
        return Task.FromResult<IReadOnlyList<ResourceReference>>(ReadLocked(ct, () => GetReferencesCore(key, includeIncoming)));
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
        var nodePrefix = workflowId + ".";
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
                related.Add(other.StartsWith(nodePrefix, StringComparison.Ordinal) ? other.Substring(nodePrefix.Length) : other);
            }
            return (IReadOnlyList<string>)related.Distinct(StringComparer.Ordinal).ToList();
        }));
    }


    // ---- 写透收敛面（唯一写入入口；每次 SQL 变更都经此同步到 LiteDB）----

    /// <summary>按权威边集对账目标节点的入边（删残留 + 补缺失），使 LiteDB 边收敛到 SQL 推导的权威集。
    /// 不整段包外层门锁：内层 Add/Remove 各自经 <see cref="MutateAsync"/> 串行取门锁，避免「持锁内同步重入 SemaphoreSlim」自死锁。</summary>
    public async Task ApplyRelationEdgesAsync(string workflowId, string nodeKey,
        IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var targetId = NodeRelationFactory.NodeId(workflowId, nodeKey);
        var current = await GetRelationsAsync(workflowId, nodeKey, includeIncoming: true, ct: ct).ConfigureAwait(false);
        var curByKey = new Dictionary<string, NodeRelation>(StringComparer.Ordinal);
        foreach (var r in current)
            if (r is not null && string.Equals(r.TargetId, targetId, StringComparison.Ordinal))
                curByKey[RelationKey(r)] = r;
        var authByKey = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in authoritative)
            if (e is not null) authByKey.Add(RelationKey(e));

        foreach (var key in curByKey.Keys.ToList())
            if (!authByKey.Contains(key))
                await RemoveRelationAsync(workflowId, curByKey[key], ct).ConfigureAwait(false);
        foreach (var e in authoritative)
            if (e is not null && !curByKey.ContainsKey(RelationKey(e)))
                await AddRelationAsync(workflowId, e, ct).ConfigureAwait(false);
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

    /// <summary>task 删除写透：级联移除 task 端点及其全部边。</summary>
    public Task RemoveTaskAsync(string taskKey, CancellationToken ct = default)
        => RemoveResourceAsync(new ResourceIdentifier { ResourceType = NodeResourceTypes.Task, Id = taskKey }, ct);

    /// <summary>清空图（全量重建原语）：删除全部边文档 + 清空会话内存端点注册表/邻接。
    /// 供守卫 RebuildAllAsync 使用：清空后从 SQL 权威重建（幂等可重复）。</summary>
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

    // ---- 唯一读面（替代 LiteDatabaseBrowser）----

    /// <summary>只读枚举 LiteDB 集合名（ur_references / ur_endpoints；供 UI 概览）。</summary>
    public IReadOnlyList<string> ListCollections()
    {
        try { return _db.GetCollectionNames().OrderBy(n => n, StringComparer.Ordinal).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// 读取某集合前 limit 个文档，按集合名分派渲染为可读文本（UI 图页只读）：
    /// 端点集合 → 端点视图（{type:id} = symbol）；引用边集合 → 边视图（src -[refType]-> tgt (strength=..)）。
    /// 修复原实现「忽略 collection 参数、恒读 ur_references」导致"集合名为 endpoints 却显示边文档"的连贯性缺陷。
    /// </summary>
    public string ReadDocuments(string collection, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(collection)) return "（空集合名）";
        var n = Math.Max(1, Math.Min(limit, 1000));
        try
        {
            if (_endpointDocs.Name.Equals(collection, StringComparison.Ordinal))
                return ReadEndpointDocs(n);
            if (_edges.Name.Equals(collection, StringComparison.Ordinal))
                return ReadEdgeDocs(n);
            return $"（未知集合：{collection}）";
        }
        catch (Exception ex)
        {
            return $"（读取失败）{ex.Message}";
        }
    }

    /// <summary>渲染引用边视图：可读行 + 原始文档 JSON（UI/诊断直接看到语义而非拼接 _id）。</summary>
    private string ReadEdgeDocs(int limit)
    {
        var docs = _edges.FindAll().Take(limit).ToList();
        if (docs.Count == 0) return "（空集合）";
        var sb = new System.Text.StringBuilder();
        foreach (var d in docs)
        {
            var src = SourceId(d) ?? string.Empty;
            var tgt = TargetId(d) ?? string.Empty;
            var rt = RefTypeOf(d) ?? string.Empty;
            sb.AppendLine($"{src} -[{rt}]-> {tgt} (strength={StrengthOf(d)})");
            sb.AppendLine(JsonSerializer.Serialize(d));
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    /// <summary>渲染端点视图：{type:id} = symbol + 原始文档 JSON。</summary>
    private string ReadEndpointDocs(int limit)
    {
        var docs = _endpointDocs.FindAll().Take(limit).ToList();
        if (docs.Count == 0) return "（空集合）";
        var sb = new System.Text.StringBuilder();
        foreach (var d in docs)
        {
            var key = d["_id"].IsString ? d["_id"].AsString : string.Empty;
            var symbol = d.TryGetValue("Symbol", out var sv) && sv.IsString ? sv.AsString : string.Empty;
            sb.AppendLine($"{key}  =  {symbol}");
            sb.AppendLine(JsonSerializer.Serialize(d));
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    // ---- 图算法（邻接/路径/依赖，供诊断与影响链查询）----

    public IReadOnlyList<string> GetNeighbors(string key) => ReadLocked(CancellationToken.None, () => _graph.GetNeighbors(key));

    public Task<IReadOnlyList<string[]>> FindPathsAsync(string fromKey, string toKey, int maxDepth = 5, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string[]>>(ReadLocked(ct, () =>
        {
            var paths = new List<string[]>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { fromKey };
            var current = new List<string> { fromKey };
            void Dfs(string node)
            {
                if (node == toKey) { if (current.Count >= 2) paths.Add(current.ToArray()); return; }
                if (current.Count >= maxDepth) return;
                foreach (var neighbor in _graph.GetOutgoingEdges(node).Select(e => e.Target))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!visited.Add(neighbor)) continue;
                    current.Add(neighbor);
                    Dfs(neighbor);
                    current.RemoveAt(current.Count - 1);
                    visited.Remove(neighbor);
                }
            }
            Dfs(fromKey);
            return (IReadOnlyList<string[]>)paths;
        }));

    public Task<IReadOnlyList<string>> GetDependenciesAsync(string key, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(ReadLocked(ct, () =>
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(key);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var node = stack.Pop();
                foreach (var e in _graph.GetOutgoingEdges(node))
                    if (reachable.Add(e.Target)) stack.Push(e.Target);
            }
            return (IReadOnlyList<string>)reachable.ToList();
        }));


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

    private async Task MutateAsync(CancellationToken ct, Func<Task> body)
    {
        if (!await _gate.WaitAsync(GateLeaseTimeout, ct).ConfigureAwait(false))
            throw new TimeoutException($"LiteDB 图写门等待超时（{GateLeaseTimeout.TotalSeconds}s），疑似写门被长持未释放。");
        try { await body().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

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
        => $"{sourceKey}{EdgeKeySeparator}{targetKey}{EdgeKeySeparator}{refType}";

    private void EnsureNodeInMemory(string key)
    {
        if (_graph.ContainsNode(key)) return;
        _graph.AddNode(key, _endpoints.TryGetValue(key, out var r) ? r : new Resource { Identifier = KeyToIdentifier(key) });
    }

    private static string KeyOfType(ResourceIdentifier id)
        => (id.ResourceType ?? string.Empty) + ResourceIdentifier.UniqueKeySeparator + (id.Id ?? string.Empty);

    private static NodeRelationType? ParseNodeRelationType(string refType)
    {
        if (refType.StartsWith(ReferenceTypes.KeyMappingToNode, StringComparison.Ordinal)) return NodeRelationType.KeyMappingToNode;
        if (refType.StartsWith(ReferenceTypes.NodeToNode, StringComparison.Ordinal)) return NodeRelationType.NodeToNode;
        return null;
    }

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

    /// <summary>持久化端点符号/metadata 到 ur_endpoints（可重建缓存，权威仍在 SQL）。</summary>
    private void PersistEndpointDoc(string key, Resource resource, ResourceIdentifier id)
    {
        var doc = new BsonDocument
        {
            ["_id"] = key,
            ["ResourceType"] = id.ResourceType ?? string.Empty,
            ["Id"] = id.Id ?? string.Empty,
            ["Symbol"] = resource.Symbol ?? string.Empty,
            ["UpdatedAt"] = DateTime.UtcNow,
        };
        if (resource.Metadata is { Count: > 0 })
        {
            var md = new BsonDocument();
            foreach (var kv in resource.Metadata)
                md[kv.Key] = ToBsonValue(kv.Value);
            doc["Metadata"] = md;
        }
        _endpointDocs.Upsert(doc);
    }

    private static BsonValue ToBsonValue(object? value)
    {
        if (value is null) return BsonValue.Null;
        return value switch
        {
            bool b => b,
            int i => i,
            long l => l,
            double d => d,
            decimal m => (double)m,
            DateTime dt => dt,
            string s => s,
            _ => value.ToString() ?? string.Empty,
        };
    }

    // ---- 边文档编解码（ur_references）----

    private static BsonDocument ToEdgeDoc(string sourceKey, ResourceIdentifier src, string targetKey, ResourceIdentifier tgt,
        string refType, double strength) => new()
        {
            ["_id"] = BuildEdgeId(sourceKey, targetKey, refType),
            ["SourceId"] = sourceKey,
            ["SourceType"] = src.ResourceType ?? (BsonValue)BsonValue.Null,
            ["TargetId"] = targetKey,
            ["TargetType"] = tgt.ResourceType ?? (BsonValue)BsonValue.Null,
            ["RefType"] = refType,
            ["Strength"] = strength,
        };

    private static string? SourceId(BsonDocument d) => d["SourceId"].IsNull ? null : d["SourceId"].AsString;
    private static string? TargetId(BsonDocument d) => d["TargetId"].IsNull ? null : d["TargetId"].AsString;
    private static string? RefTypeOf(BsonDocument d) => d["RefType"].IsNull ? null : d["RefType"].AsString;
    private static double StrengthOf(BsonDocument d) => d["Strength"].IsNumber ? d["Strength"].AsDouble : 1.0;
}

