using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.HierarchySupport;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Relations;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Share.TaskStatusPool.TaskPool;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.HierarchyMethod.Nodes.TaskGraph
{
    /// <summary>
    /// 通用数据处理模块 —— 「创建 task + 维护引用关系」的确定性复合服务（Agent 无关，非专属智能体）。
    /// 承载「引用关系第一性原理」里确定性关系图 = 原生"身体/脑干"的职责：把任务创建与节点/引用边维护收拢为
    /// 原子复合能力（复用 <see cref="ITaskGraphTaskStore"/>（任务入队端口，Agent.Core 适配）+ <see cref="IResourceReferenceStore"/>
    /// （ur_resources / ur_references 引用图，LiteDB 关系投影））。写边经 IResourceReferenceStore（协调器）原子幂等，
    /// 本服务只编排（SingleImplementation / TH-12）。
    /// 依赖均可选注入：未装配的任务端口/关系库返回明确诊断而非抛错（对齐「未装配时返回诊断，不消耗 AI 额度」模式），
    /// 保证模块在 DB-only / 无任务池路径下仍安全可测。
    /// </summary>
    public sealed class TaskGraphService
    {
        private const string DefaultResourceType = "node";

        // 任务 → 关联节点 的归属边类型（A1：字面量唯一源 = ReferenceTypes，不再本地写字面量）。
        // 注意：与 ReferenceTypes.NodeToNode（节点↔节点端口数据流组合边）**语义不同**（虽字符串同值），
        // 勿据此与端口流边混用/合并查询（避免图边语义碰撞）。
        private const string TaskToNodeRefType = ReferenceTypes.NodeToNode;

        private readonly ITaskGraphTaskStore? _taskPool;
        private readonly IResourceReferenceStore? _resources;
        private readonly ITaskRouteResolver? _routeResolver;
        private readonly IHierarchyReferenceManager? _hierarchy;
        private readonly IRelationPorts? _ports;

        /// <param name="taskPool">任务入队端口（可选；提供 task 入队/状态/删除，由 Agent.Core 适配）。</param>
        /// <param name="resources">资源/引用关系存储（可选；提供节点与引用边读写，LiteDB 关系投影）。</param>
        /// <param name="routeResolver">路由解析端口（可选；标签/执行腿 → 执行腿，唯一实现 = Agent.Core.SubAgentRouteResolver）。</param>
        /// <param name="hierarchy">层级引用管理端口（可选；history 归属唯一写原语 = AttachHistoryTaskAsync）。</param>
        /// <param name="ports">关系端口（可选；缺省从 <paramref name="hierarchy"/> 的租约提供者取）。</param>
        public TaskGraphService(
            ITaskGraphTaskStore? taskPool = null,
            IResourceReferenceStore? resources = null,
            ITaskRouteResolver? routeResolver = null,
            IHierarchyReferenceManager? hierarchy = null,
            IRelationPorts? ports = null)
        {
            _taskPool = taskPool;
            _resources = resources;
            _routeResolver = routeResolver;
            _hierarchy = hierarchy;
            _ports = ports ?? (hierarchy as IRelationPortsProvider)?.Ports;
        }

        /// <summary>入队任务并激活消费（写 ur_tasks 即激活 AI 循环）。任务端口未装配返回 -1 供调用方诊断。</summary>
        public Task<long> EnqueueTaskAsync(TaskGraphTaskSpec spec, CancellationToken ct = default)
        {
            if (_taskPool is null)
                return Task.FromResult(-1L); // 未装配任务端口：调用方据 -1 给出诊断，不消耗额度
            return _taskPool.EnqueueAsync(spec, ct);
        }

        /// <summary>
        /// 创建任务 + 关联节点 + 引用边（复合顺序编排）。顺序：写任务节点 + 关联节点（幂等）→ 建引用边
        /// （<see cref="IResourceReferenceStore.AddReferenceAsync"/> 端点为 SQL 实体，存在性由 SQL 写透时序保证，
        /// 协调器不 fail-fast 校验端点）→ 入队任务。
        /// 失败语义：节点/引用边失败**不回滚**，仅逐条记入 <see cref="TaskGraphResult.Diagnostics"/>（已完成节点保留）；
        /// 未装配任务端口时入队返回 -1 并记诊断。调用方须据 Diagnostics 判定部分成功，不可假定整体原子。
        /// </summary>
        public async Task<TaskGraphResult> CreateTaskWithRelationsAsync(
            CreateTaskGraphRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.TaskKey)) throw new ArgumentException("TaskKey 不能为空", nameof(request));
            var diagnostics = new List<string>();
            var createdNodes = new List<string>();

            // 0) 路由前置校验（未知标签/冲突声明 ⇒ 不写图、不入队，禁半成品）
            var route = ResolveRoute(request, diagnostics);
            if (!route.Ok)
            {
                diagnostics.Add(route.Code!);
                diagnostics.Add("route_rejected");
                return new TaskGraphResult(request.TaskKey, -1L, Array.Empty<string>(), Array.Empty<ReferenceEdge>(), diagnostics);
            }

            // 1) 任务节点 + 关联节点（幂等：已存在跳过）
            var taskResourceType = NonBlank(request.ResourceType) ?? DefaultResourceType;
            await EnsureResourceAsync(new NodeSpec(taskResourceType, request.TaskKey, request.TaskKey), createdNodes, diagnostics, ct).ConfigureAwait(false);
            foreach (var node in request.RelatedNodes ?? Array.Empty<NodeSpec>())
                await EnsureResourceAsync(node, createdNodes, diagnostics, ct).ConfigureAwait(false);

            // 2) 引用边：优先显式 Edges；否则默认 task → 各关联节点。只收集「真实写入成功」的边。
            var candidates = (request.Edges is { Count: > 0 } ? request.Edges : DefaultEdges(taskResourceType, request.TaskKey, request.RelatedNodes))
                .ToList();
            var writtenEdges = new List<ReferenceEdge>();
            foreach (var edge in candidates)
            {
                if (await EnsureReferenceAsync(edge, diagnostics, ct).ConfigureAwait(false))
                    writtenEdges.Add(edge);
            }

            // 3) history 归属（成员可见性）：唯一写原语 = IHierarchyReferenceManager.AttachHistoryTaskAsync
            //    （写边由 Attach 承担；本服务不再把该边混入 Edges 结果集，避免消费方重复计数）。
            var historyId = NonBlank(request.HistoryId);
            var historyAttached = false;
            if (historyId is not null)
            {
                if (_hierarchy is null)
                {
                    diagnostics.Add("history_linker_unattached");
                    return new TaskGraphResult(request.TaskKey, -1L, createdNodes, writtenEdges, diagnostics);
                }
                try
                {
                    await _hierarchy.AttachHistoryTaskAsync(historyId, request.TaskKey, ct).ConfigureAwait(false);
                    historyAttached = true;
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"history_attach_failed:{ex.Message}");
                    return new TaskGraphResult(request.TaskKey, -1L, createdNodes, writtenEdges, diagnostics);
                }
            }

            // 4) 入队任务（激活消费）
            long enqueued = -1;
            if (_taskPool is not null)
            {
                var spec = new TaskGraphTaskSpec(
                    TaskKey: request.TaskKey,
                    UserInput: request.UserInput,
                    ThreadId: request.ThreadId,
                    InputKey: request.InputKey,
                    Priority: request.Priority,
                    MaxRetries: request.MaxRetries,
                    SchedulingMode: request.SchedulingMode ?? SchedulingMode.Hybrid,
                    SchedulerRulesKey: request.SchedulerRulesKey,
                    SubAgentTag: NonBlank(request.SubAgentTag),
                    ExecutorId: route.ExecutorId,
                    HistoryId: historyId,
                    RelatedNodes: request.RelatedNodes?.Select(n => n.Id).ToArray(),
                    Active: true);
                enqueued = await _taskPool.EnqueueAsync(spec, ct).ConfigureAwait(false);
            }
            else
            {
                // 结构诊断码（供上层判定，不靠自然语言）：
                diagnostics.Add("task_port_unattached");
            }

            // 未真正入队（未装配端口 / 入队失败）⇒ 明确标记，调用方据此判失败，禁把半边图当成功。
            if (enqueued < 0)
                diagnostics.Add("task_enqueue_failed");

            return new TaskGraphResult(request.TaskKey, enqueued, createdNodes, writtenEdges, diagnostics, historyAttached);
        }
        /// <summary>维护引用边（remove=false 增 / true 删）。关系库未装配返回诊断而非抛错。</summary>
        public async Task<IReadOnlyList<string>> MaintainReferencesAsync(
            IEnumerable<ReferenceEdge> edges, bool remove = false, CancellationToken ct = default)
        {
            var diagnostics = new List<string>();
            if (edges is null) return diagnostics;
            foreach (var edge in edges)
            {
                try
                {
                    await ApplyEdgeAsync(edge, remove, diagnostics, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"引用边 {edge.SourceId} -[{edge.RefType}]-> {edge.TargetId} 失败: {ex.Message}");
                }
            }
            return diagnostics;
        }

        /// <summary>查询任务图：任务状态 + 任务节点邻接引用边。未装配时返回诊断。</summary>
        public async Task<TaskGraphSnapshot> GetTaskGraphAsync(string taskKey, CancellationToken ct = default)
        {
            var diagnostics = new List<string>();
            var status = TaskExecutionStatus.NotStarted;
            if (_taskPool is not null)
                status = await _taskPool.GetStatusAsync(taskKey, ct).ConfigureAwait(false);
            else
                diagnostics.Add("任务端口未装配：任务状态不可查");

            var edges = new List<ReferenceEdge>();
            if (_resources is not null)
            {
                var refs = await _resources.GetReferencesAsync(To(DefaultResourceType, taskKey), includeIncoming: true, ct).ConfigureAwait(false);
                foreach (var r in refs)
                    edges.Add(new ReferenceEdge(r.Source.ResourceType ?? DefaultResourceType, r.Source.Id ?? string.Empty,
                        r.Target.ResourceType ?? DefaultResourceType, r.Target.Id ?? string.Empty, r.RefType, r.Strength));
            }
            else
            {
                diagnostics.Add("引用关系库未装配：引用边不可查");
            }

            var nodes = edges
                .SelectMany(e => new[] { e.SourceId, e.TargetId })
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new TaskGraphSnapshot(taskKey, status, nodes, edges, diagnostics);
        }

        /// <summary>删除任务并级联清理其节点（依赖存储的级联删引用：删资源自动清入/出边）。</summary>
        public async Task<IReadOnlyList<string>> DeleteTaskGraphAsync(string taskKey, CancellationToken ct = default)
        {
            var diagnostics = new List<string>();
            if (_taskPool is not null)
                await _taskPool.DeleteAsync(taskKey, ct).ConfigureAwait(false);
            else
                diagnostics.Add("任务端口未装配：跳过 task 删除");

            if (_resources is not null)
                await _resources.RemoveResourceAsync(To(DefaultResourceType, taskKey), ct).ConfigureAwait(false);
            else
                diagnostics.Add("引用关系库未装配：跳过节点删除");
            return diagnostics;
        }

        // ---- 内部确定性助手 ----

        /// <summary>路由解析（tag/executor 全空 ⇒ 旧行为；端口未装配且有路由诉求 ⇒ fail-closed）。</summary>
        private TaskRoute ResolveRoute(CreateTaskGraphRequest request, List<string> diagnostics)
        {
            var tag = NonBlank(request.SubAgentTag);
            var executor = NonBlank(request.ExecutorId);
            if (tag is null && executor is null) return TaskRoute.Allow(null);
            if (_routeResolver is null)
            {
                // 无 resolver 就允许 ⇒ 可能入队「无执行腿」任务并静默回落（ANTI-SILENT-FALLBACK）。
                diagnostics.Add("route_resolver_unattached");
                return TaskRoute.Reject("route_resolver_unattached");
            }
            return _routeResolver.Resolve(tag, executor);
        }

        private async Task EnsureResourceAsync(NodeSpec node, List<string> created, List<string> diagnostics, CancellationToken ct)
        {
            if (_resources is null)
            {
                diagnostics.Add($"引用关系库未装配：跳过节点 {node.ResourceType}:{node.Id}");
                return;
            }
            var rid = To(node.ResourceType, node.Id);
            if (await _resources.GetResourceAsync(rid, ct).ConfigureAwait(false) is not null)
                return; // 幂等：已存在跳过
            try
            {
                await _resources.AddResourceAsync(rid, metadata: null, symbol: NonBlank(node.Name), ct).ConfigureAwait(false);
                created.Add(node.Id);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"节点 {node.ResourceType}:{node.Id} 写入失败: {ex.Message}");
            }
        }

        private async Task<bool> EnsureReferenceAsync(ReferenceEdge edge, List<string> diagnostics, CancellationToken ct)
            => await ApplyEdgeAsync(edge, remove: false, diagnostics, ct).ConfigureAwait(false);

        /// <summary>
        /// 边写唯一实现：归属边 → 声明 operation（内核）；数据流边 → 图原语；未声明归属边 ⇒ 拒绝（禁裸写）。
        /// </summary>
        private async Task<bool> ApplyEdgeAsync(ReferenceEdge edge, bool remove, List<string> diagnostics, CancellationToken ct)
        {
            var operation = ResolveOwnershipOperation(edge, remove);
            if (operation is not null)
            {
                var request = new RelationRequest(edge.SourceId, TargetKey: edge.TargetId);
                var result = remove
                    ? await _ports!.Commands.RemoveAsync(operation, request, ct).ConfigureAwait(false)
                    : await _ports!.Commands.EnsureAsync(operation, request, ct).ConfigureAwait(false);
                if (result.Ok) return true;

                var detail = result.Diagnostics.Count > 0 ? string.Join("; ", result.Diagnostics) : result.Outcome.ToString();
                diagnostics.Add($"引用边 {edge.SourceId} -[{edge.RefType}]-> {edge.TargetId} 失败: {detail}");
                return false;
            }

            // 已装配端口但既非声明归属边、也非数据流边 ⇒ fail-closed（禁绕过内核写归属边）。
            if (_ports is not null && !_ports.Operations.IsDataflowEdge(edge.RefType))
            {
                diagnostics.Add($"undeclared-ownership-edge:{edge.RefType}");
                return false;
            }

            if (_resources is null)
            {
                diagnostics.Add($"引用关系库未装配：跳过引用边 {edge.SourceId} -> {edge.TargetId}");
                return false;
            }

            try
            {
                // 数据流边（node_to_node provenance / keymapping_to_node）：端点为 SQL 实体，存在性由 SQL 写透时序保证。
                if (remove)
                    await _resources.RemoveReferenceAsync(To(edge.SourceResourceType, edge.SourceId), To(edge.TargetResourceType, edge.TargetId), edge.RefType, ct).ConfigureAwait(false);
                else
                    await _resources.AddReferenceAsync(To(edge.SourceResourceType, edge.SourceId), To(edge.TargetResourceType, edge.TargetId), edge.RefType, edge.Strength, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"引用边 {edge.SourceId} -[{edge.RefType}]-> {edge.TargetId} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>归属边 → 声明 operation（0/多义 ⇒ null）；裸 <c>node_to_node</c> + task→node 归到 mount/unmount_node_task。</summary>
        private string? ResolveOwnershipOperation(ReferenceEdge edge, bool remove)
        {
            if (_ports is null) return null;

            var declared = _ports.Operations.ResolveByEdge(edge.RefType, remove);
            if (declared is not null)
            {
                if (!string.Equals(declared.Source, edge.SourceResourceType, StringComparison.Ordinal)
                    || !string.Equals(declared.Target, edge.TargetResourceType, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"关系端点与声明不符：{edge.SourceResourceType}->{edge.TargetResourceType}（operation={declared.Id}）");
                }
                return declared.Id;
            }

            if (string.Equals(edge.RefType, ReferenceTypes.NodeToNode, StringComparison.Ordinal)
                && string.Equals(edge.SourceResourceType, "task", StringComparison.Ordinal)
                && string.Equals(edge.TargetResourceType, "node", StringComparison.Ordinal))
            {
                return remove ? RelationOperationIds.UnmountNodeTask : RelationOperationIds.MountNodeTask;
            }

            return null;
        }

        private static IEnumerable<ReferenceEdge> DefaultEdges(string resourceType, string taskKey, IReadOnlyList<NodeSpec>? related)
            => (related ?? Array.Empty<NodeSpec>())
                .Where(n => !string.IsNullOrWhiteSpace(n.Id) && !string.Equals(n.Id, taskKey, StringComparison.Ordinal))
                .Select(n => new ReferenceEdge(resourceType, taskKey, n.ResourceType, n.Id, TaskToNodeRefType));

        private static ResourceIdentifier To(string resourceType, string id)
            => new() { ResourceType = NonBlank(resourceType) ?? DefaultResourceType, Id = id };

        private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>创建任务 + 引用关系的复合请求（通用数据处理模块的输入端契约）。</summary>
    public sealed record CreateTaskGraphRequest(
        string TaskKey,
        string? UserInput = null,
        string? ResourceType = null,
        int Priority = 0,
        int MaxRetries = 0,
        SchedulingMode? SchedulingMode = null,
        string? SchedulerRulesKey = null,
        string? ThreadId = null,
        string? InputKey = null,
        IReadOnlyList<NodeSpec>? RelatedNodes = null,
        IReadOnlyList<ReferenceEdge>? Edges = null,
        string? SubAgentTag = null,
        string? ExecutorId = null,
        string? HistoryId = null);

    /// <summary>一个资源节点（资源类型 + Id + 可选符号名）。</summary>
    public sealed record NodeSpec(string ResourceType, string Id, string? Name = null);

    /// <summary>一条引用边 src -[refType]-> tgt。</summary>
    public sealed record ReferenceEdge(
        string SourceResourceType, string SourceId,
        string TargetResourceType, string TargetId,
        string RefType, double Strength = 1.0);

    /// <summary>复合创建的结果。</summary>
    public sealed record TaskGraphResult(
        string TaskKey,
        long Enqueued,
        IReadOnlyList<string>? CreatedNodes = null,
        IReadOnlyList<ReferenceEdge>? Edges = null,
        IReadOnlyList<string>? Diagnostics = null,
        bool HistoryAttached = false);

    /// <summary>任务图查询快照（任务状态 + 节点 + 引用边）。</summary>
    public sealed record TaskGraphSnapshot(
        string TaskKey,
        TaskExecutionStatus Status,
        IReadOnlyList<string> Nodes,
        IReadOnlyList<ReferenceEdge> Edges,
        IReadOnlyList<string>? Diagnostics = null);
}
