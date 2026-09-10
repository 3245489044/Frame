#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using Modules.GraphProcessing.Algorithms;
using Modules.GraphProcessing.Snapshots;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.BatchIntegration
{
    /// <summary>
    /// 批次图服务：组合 GraphProcessing（Algorithms/Snapshots）+ 引用图，提供批次环检测/快照/差异/链接能力。
    /// </summary>
    public sealed class BatchGraphService
    {
        private readonly IUniversalReferenceManager _manager;
        private readonly BatchRegistry _registry;

        public BatchGraphService(IUniversalReferenceManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _registry = new BatchRegistry(manager);
        }

        /// <summary>建立批次间依赖引用。</summary>
        public Task LinkBatchesAsync(string fromBatchId, string toBatchId,
            ReferenceType relationType = ReferenceType.Dependency,
            string? description = null)
        {
            return _registry.LinkBatchesAsync(fromBatchId, toBatchId, relationType, description);
        }

        /// <summary>检测批次依赖图中的环路径（GraphProcessing.Algorithms）。</summary>
        public async Task<IReadOnlyList<IReadOnlyList<string>>> DetectBatchCyclesAsync()
        {
            using var graph = await BuildBatchGraphAsync().ConfigureAwait(false);
            var detector = new DefaultCycleDetector<string>();
            return detector.FindCycles(
                graph.GetAllNodeKeys().ToList(),
                key => graph.GetOutgoingEdges(key).Select(e => e.Target.ToString() ?? string.Empty));
        }

        /// <summary>创建批次依赖图快照（GraphProcessing.Snapshots）。</summary>
        public async Task<GraphSnapshot<BatchMetadata, object>> CreateBatchSnapshotAsync()
        {
            using var graph = await BuildBatchGraphAsync().ConfigureAwait(false);
            return GraphSnapshot<BatchMetadata, object>.CreateFrom(graph, "batch-graph");
        }

        /// <summary>比对两份批次快照差异（GraphProcessing.Snapshots.GraphDiff）。</summary>
        public Task<GraphDiff<BatchMetadata, object>> CompareBatchSnapshotsAsync(
            GraphSnapshot<BatchMetadata, object> oldSnapshot,
            GraphSnapshot<BatchMetadata, object> newSnapshot)
        {
            if (oldSnapshot == null) throw new ArgumentNullException(nameof(oldSnapshot));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));
            return Task.FromResult(GraphDiff<BatchMetadata, object>.Calculate(oldSnapshot, newSnapshot));
        }

        private async Task<ICoreGraph<string, BatchMetadata, object>> BuildBatchGraphAsync()
        {
            var graph = new CoreGraph<string, BatchMetadata, object>();
            var allMeta = BatchManager.Instance.GetAllBatches().ToList();

            foreach (var meta in allMeta)
            {
                if (!graph.ContainsNode(meta.BatchId))
                    graph.AddNode(meta.BatchId, meta);
            }

            var seen = new HashSet<string>();
            foreach (var meta in allMeta)
            {
                var deps = await _registry.GetBatchDependenciesAsync(meta.BatchId).ConfigureAwait(false);
                foreach (var depId in deps)
                {
                    if (!graph.ContainsNode(depId)) continue;

                    var edgeId = $"{meta.BatchId}->{depId}";
                    if (!seen.Add(edgeId)) continue;
                    graph.AddEdge(edgeId, meta.BatchId, depId, isDirected: true, data: null, weight: 1.0);
                }
            }

            return graph;
        }
    }
}
