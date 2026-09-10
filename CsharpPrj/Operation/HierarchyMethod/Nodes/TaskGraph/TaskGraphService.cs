using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Share.HierarchySupport;
using Operation.DataStoreDriven.Abstractions;
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

        // 任务 → 关联节点 的归属边类型。注意：与 ReferenceTypes.NodeToNode（节点↔节点端口数据流组合边）语义不同，
        // 虽字符串同值但用途各异，勿据此与端口流边混用/合并查询（避免图边语义碰撞）。
        private const string TaskToNodeRefType = "node_to_node";

        private readonly ITaskGraphTaskStore? _taskPool;
        private readonly IResourceReferenceStore? _resources;

        /// <param name="taskPool">任务入队端口（可选；提供 task 入队/状态/删除，由 Agent.Core 适配）。</param>
        /// <param name="resources">资源/引用关系存储（可选；提供节点与引用边读写，LiteDB 关系投影）。</param>
        public TaskGraphService(ITaskGraphTaskStore? taskPool = null, IResourceReferenceStore? resources = null)
        {
            _taskPool = taskPool;
            _resources = resources;
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
        /// （<see cref="IResourceReferenceStore.AddReferenceAsync"/> 端点缺失时由存储侧 fail-fast 抛错）→ 入队任务。
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

            // 1) 任务节点 + 关联节点（幂等：已存在跳过）
            var taskResourceType = NonBlank(request.ResourceType) ?? DefaultResourceType;
            await EnsureResourceAsync(new NodeSpec(taskResourceType, request.TaskKey, request.TaskKey), createdNodes, diagnostics, ct).ConfigureAwait(false);
            foreach (var node in request.RelatedNodes ?? Array.Empty<NodeSpec>())
                await EnsureResourceAsync(node, createdNodes, diagnostics, ct).ConfigureAwait(false);

            // 2) 引用边：优先显式 Edges；否则默认 task → 各关联节点
            var edges = (request.Edges is { Count: > 0 } ? request.Edges : DefaultEdges(taskResourceType, request.TaskKey, request.RelatedNodes))
                .ToList();
            foreach (var edge in edges)
                await EnsureReferenceAsync(edge, diagnostics, ct).ConfigureAwait(false);

            // 3) 入队任务（激活消费）
            long enqueued = -1;
            if (_taskPool is not null)
            {
                var spec = new TaskGraphTaskSpec(
                    request.TaskKey,
                    request.UserInput,
                    request.ThreadId,
                    request.InputKey,
                    request.Priority,
                    request.MaxRetries,
                    request.SchedulingMode ?? SchedulingMode.Hybrid,
                    request.SchedulerRulesKey);
                enqueued = await _taskPool.EnqueueAsync(spec, ct).ConfigureAwait(false);
            }
            else
            {
                diagnostics.Add($"任务端口未装配：跳过 task 入队（仅写入引用图），taskKey={request.TaskKey}");
            }

            return new TaskGraphResult(request.TaskKey, enqueued, createdNodes, edges, diagnostics);
        }
        /// <summary>维护引用边（remove=false 增 / true 删）。关系库未装配返回诊断而非抛错。</summary>
        public async Task<IReadOnlyList<string>> MaintainReferencesAsync(
            IEnumerable<ReferenceEdge> edges, bool remove = false, CancellationToken ct = default)
        {
            var diagnostics = new List<string>();
            if (_resources is null)
            {
                diagnostics.Add("引用关系库未装配：跳过引用边维护");
                return diagnostics;
            }
            if (edges is null) return diagnostics;
            foreach (var edge in edges)
            {
                try
                {
                    if (remove)
                        await _resources.RemoveReferenceAsync(To(edge.SourceResourceType, edge.SourceId), To(edge.TargetResourceType, edge.TargetId), edge.RefType, ct).ConfigureAwait(false);
                    else
                        await _resources.AddReferenceAsync(To(edge.SourceResourceType, edge.SourceId), To(edge.TargetResourceType, edge.TargetId), edge.RefType, edge.Strength, ct).ConfigureAwait(false);
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

        private async Task EnsureReferenceAsync(ReferenceEdge edge, List<string> diagnostics, CancellationToken ct)
        {
            if (_resources is null)
            {
                diagnostics.Add($"引用关系库未装配：跳过引用边 {edge.SourceId} -> {edge.TargetId}");
                return;
            }
            try
            {
                // fail-fast（StructuralGrounding）：端点节点不存在时抛 InvalidOperationException，由调用方整体回滚。
                await _resources.AddReferenceAsync(To(edge.SourceResourceType, edge.SourceId), To(edge.TargetResourceType, edge.TargetId), edge.RefType, edge.Strength, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"引用边 {edge.SourceId} -[{edge.RefType}]-> {edge.TargetId} 失败: {ex.Message}");
            }
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
        IReadOnlyList<ReferenceEdge>? Edges = null);

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
        IReadOnlyList<string>? Diagnostics = null);

    /// <summary>任务图查询快照（任务状态 + 节点 + 引用边）。</summary>
    public sealed record TaskGraphSnapshot(
        string TaskKey,
        TaskExecutionStatus Status,
        IReadOnlyList<string> Nodes,
        IReadOnlyList<ReferenceEdge> Edges,
        IReadOnlyList<string>? Diagnostics = null);
}
