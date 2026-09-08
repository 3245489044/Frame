using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.WorkFlowMethod.Nodes.Contracts;

namespace Operation.HierarchyMethod.Nodes {
    /// <summary>
    /// 节点输出存储（输出并入业务节点，消除 NodeType=output 独立记录双写）—— 节点即存储单元的默认落地实现（存储收敛：模块 A 并入模块 C）。
    /// 全部节点输出写入业务节点（NodeType=node，NodeKey=裸 {nodeKey}，PayloadJson=BusinessNodePayload{ Symbol, Outputs }），
    /// 与 NodeExecutionService.PersistNodeReferencesAsync 写标识/关系读-改-写互不覆盖；端口以 JSON 载荷承载、值经 <see cref="NodeOutputCodec"/>
    /// 类型保真往返；数据载荷持久化、重启可恢复。检索按 ThreadId 过滤 + afterId 分页。
    /// 从 Agent.Core 下沉（SingleImplementation / TH-12）：实现 <see cref="INodeOutputStore"/> 端口，框架 LLM-free、零 Agent 依赖。
    /// </summary>
    public sealed class NodeOutputStore : INodeOutputStore
    {
        private readonly INodeStore _nodes;
        private readonly string _defaultWorkflowId;

        public NodeOutputStore(INodeStore nodes, string defaultWorkflowId = NodeKeys.DefaultWorkflowId)
        {
            _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            // 实例默认经单点归一化（空 → NodeKeys.DefaultWorkflowId），NormalizeWf 据此按实例默认回退（保实例默认语义、无行为漂移）。
            _defaultWorkflowId = NodeOutputKeys.Normalize(defaultWorkflowId);
        }

        public Task InitializeAsync(CancellationToken ct = default)
            => Task.CompletedTask; // ur_nodes 由 SqliteNodeStore.InitializeAsync 幂等建表

        public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default)
            => WriteOutputAsync(workflowId, nodeKey, port, value, OutputVisibility.WorkflowLocal, ct);

        /// <summary>
        /// 写单个节点输出端口（输出并入业务节点，消除 NodeType=output 独立记录双写）：
        /// 幂等 upsert by (nodeKey) —— 同端口重复写覆盖同值，不产生重复记录。
        /// 写业务节点（NodeType=node，NodeKey=裸 {node}，PayloadJson=BusinessNodePayload），读-改-写保留
        /// 既有 Symbol/RelationsJson/Status（与 NodeExecutionService.PersistNodeReferencesAsync 写标识/关系互不覆盖）。
        /// <paramref name="visibility"/> 声明节点可见性（对齐 C# 作用域/public，跨工作流访问须 Exported）。
        /// </summary>
        public async Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(port);
            if (value is null) return;
            var wf = NormalizeWf(workflowId);
            var existing = await _nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
            var payload = existing != null ? NodeOutputCodec.DecodeBusiness(existing.PayloadJson) : new BusinessNodePayload();
            payload.Outputs ??= new NodeOutputDoc();
            payload.Outputs.Ports[port] = NodeOutputCodec.EncodeValue(value);
            payload.Outputs.Visibility = visibility.ToString();
            await _nodes.PutAsync(new NodeRecord
            {
                NodeKey = nodeKey,
                NodeType = NodeTypes.Node,
                ThreadId = wf,
                Status = existing?.Status,
                PayloadJson = NodeOutputCodec.EncodeBusiness(payload),
                RelationsJson = existing?.RelationsJson,
            }, ct).ConfigureAwait(false);
        }

        public async Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        {
            var wf = NormalizeWf(workflowId);
            var rec = await _nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
            if (rec == null) return null;
            return BuildOutput(wf, nodeKey, rec.PayloadJson);
        }

        public async Task<NodeOutput?> TryGetNodeOutputScopedAsync(string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default)
        {
            var output = await TryGetNodeOutputAsync(workflowId, nodeKey, ct).ConfigureAwait(false);
            if (output is null) return null;
            return OutputScopeResolver.IsVisibleTo(workflowId, output.Visibility, requestingWorkflowId) ? output : null;
        }

        public async Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default)
        {
            var wf = NormalizeWf(workflowId);
            var result = new List<NodeOutput>();
            foreach (var rec in await ListNodesAsync(wf, ct).ConfigureAwait(false))
            {
                var output = BuildOutput(wf, rec.NodeKey, rec.PayloadJson);
                if (output is not null) result.Add(output);
            }
            return result;
        }

        public async Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(string workflowId, string requestingWorkflowId, CancellationToken ct = default)
        {
            var result = new List<NodeOutput>();
            foreach (var output in await EnumerateOutputsAsync(workflowId, ct).ConfigureAwait(false))
                if (OutputScopeResolver.IsVisibleTo(workflowId, output.Visibility, requestingWorkflowId))
                    result.Add(output);
            return result;
        }

        public async Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default)
        {
            var wf = NormalizeWf(workflowId);
            var records = await ListNodesAsync(wf, ct).ConfigureAwait(false);
            return records.Select(r => r.NodeKey).Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>写入/更新单个节点的信号元数据（读-改-写保留既有输出端口；仅更新节点信号字段）。</summary>
        public async Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(nodeKey);
            ArgumentNullException.ThrowIfNull(signal);
            var wf = NormalizeWf(workflowId);
            var existing = await _nodes.GetAsync(nodeKey, ct).ConfigureAwait(false);
            var payload = existing != null ? NodeOutputCodec.DecodeBusiness(existing.PayloadJson) : new BusinessNodePayload();
            payload.Outputs ??= new NodeOutputDoc();
            var doc = payload.Outputs;
            doc.Module = signal.Module;
            doc.Region = signal.Region;
            doc.NodeType = signal.NodeType;
            doc.Freshness = signal.Freshness;
            doc.ValueScore = signal.ValueScore;
            doc.OutputDigest = signal.OutputDigest;
            doc.Sticky = signal.Sticky;
            doc.Priority = signal.Priority;
            await _nodes.PutAsync(new NodeRecord
            {
                NodeKey = nodeKey,
                NodeType = NodeTypes.Node,
                ThreadId = wf,
                Status = existing?.Status,
                PayloadJson = NodeOutputCodec.EncodeBusiness(payload),
                RelationsJson = existing?.RelationsJson,
            }, ct).ConfigureAwait(false);
        }

        /// <summary>枚举工作流下全部节点信号摘要（NodeSignal）：从记录载荷读信号元数据 + 摘要，免读正文。
        /// 过滤器 module/nodeType/region 精确匹配；按信号 rank 排序（sticky → valueScore desc → freshness desc）；limit 截断。</summary>
        public async Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(
            string workflowId, string? module = null, string? nodeType = null, string? region = null,
            int limit = -1, CancellationToken ct = default)
        {
            var wf = NormalizeWf(workflowId);
            var signals = new List<NodeSignal>();
            foreach (var rec in await ListNodesAsync(wf, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var payload = NodeOutputCodec.DecodeBusiness(rec.PayloadJson);
                var doc = payload.Outputs;
                if (doc is null) continue;
                // 精确匹配过滤器（module/nodeType/region）。
                if (!string.IsNullOrWhiteSpace(module) && !string.Equals(doc.Module, module, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(nodeType) && !string.Equals(doc.NodeType, nodeType, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(region) && !string.Equals(doc.Region, region, StringComparison.Ordinal)) continue;
                signals.Add(BuildSignal(rec.NodeKey, wf, doc));
            }
            // 信号 rank：sticky → valueScore desc → freshness desc（spec §五）。
            var ranked = signals
                .OrderByDescending(s => s.Sticky)
                .ThenByDescending(s => s.Priority)
                .ThenByDescending(s => s.ValueScore ?? double.MinValue)
                .ThenByDescending(s => s.Freshness)
                .ToList();
            return limit >= 0 ? ranked.Take(limit).ToList() : ranked;
        }

        public async Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default)
        {
            var wf = NormalizeWf(workflowId);
            await _nodes.DeleteAsync(nodeKey, ct).ConfigureAwait(false);
        }

        public async Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default)
        {
            var wf = NormalizeWf(workflowId);
            var records = await ListNodesAsync(wf, ct).ConfigureAwait(false);
            if (records.Count > 0)
                await _nodes.DeleteManyAsync(records.Select(r => r.NodeKey).ToList(), ct).ConfigureAwait(false);
        }

        /// <summary>按 NodeType=node + ThreadId 拉取该工作流下全部业务节点的记录集（输出并入业务节点后，
        /// 枚举/清理的输出载体即业务节点；每个逻辑节点一条记录，消除双写）。</summary>
        private async Task<IReadOnlyList<NodeRecord>> ListNodesAsync(string wf, CancellationToken ct)
            => await ListAllAsync(NodeTypes.Node, wf, ct).ConfigureAwait(false);

        /// <summary>分页拉取某 NodeType + ThreadId 下全部记录（afterId 游标，避免 limit 截断）。</summary>
        private async Task<IReadOnlyList<NodeRecord>> ListAllAsync(string nodeType, string threadId, CancellationToken ct)
        {
            const int limit = 200;
            // A6.5：分页护栏——最大页数上限，防底层 ListAsync 未严格按 afterId 过滤（或返回重复）时死循环。
            const int maxPages = 10_000;
            var all = new List<NodeRecord>();
            long? afterId = null;
            var pages = 0;
            while (true)
            {
                var page = await _nodes.ListAsync(nodeType, threadId, afterId, limit, ct).ConfigureAwait(false);
                all.AddRange(page);
                if (page.Count < limit) break;
                afterId = page[^1].Id;
                if (++pages >= maxPages)
                    throw new InvalidOperationException(
                        $"分页拉取超过 {maxPages} 页上限（nodeType={nodeType}, threadId={threadId}），疑似 afterId 游标未严格推进。");
            }
            return all;
        }

        private static NodeOutput? BuildOutput(string workflowId, string nodeKey, string payloadJson)
        {
            var payload = NodeOutputCodec.DecodeBusiness(payloadJson);
            var doc = payload.Outputs;
            if (doc is null || doc.Ports is not { Count: > 0 }) return null;
            var vis = Enum.TryParse<OutputVisibility>(doc.Visibility, ignoreCase: true, out var v)
                ? v
                : OutputVisibility.WorkflowLocal;
            var output = new NodeOutput
            {
                NodeKey = nodeKey,
                WorkflowId = workflowId,
                Visibility = vis,
                Module = doc.Module,
                Region = doc.Region,
                NodeType = doc.NodeType,
                Freshness = doc.Freshness,
                ValueScore = doc.ValueScore,
                OutputDigest = doc.OutputDigest,
                Sticky = doc.Sticky,
                Priority = doc.Priority,
            };
            foreach (var (port, pv) in doc.Ports)
                output.OutputPorts[port] = NodeOutputCodec.DecodeValue(pv);
            return output;
        }

        /// <summary>从记录载荷的 NodeOutputDoc 构建节点信号摘要（免读正文；信号元数据缺失时回退缺省，零回归）。</summary>
        private static NodeSignal BuildSignal(string nodeKey, string workflowId, NodeOutputDoc doc)
            => new(
                nodeKey, workflowId,
                doc.Module, doc.Region, doc.NodeType,
                doc.Freshness, doc.ValueScore, doc.OutputDigest,
                doc.Sticky, doc.Priority);

        private string NormalizeWf(string workflowId)
            => string.IsNullOrEmpty(workflowId) ? _defaultWorkflowId : workflowId;
    }
}

