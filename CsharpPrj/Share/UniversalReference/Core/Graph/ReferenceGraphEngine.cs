#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphFoundation.Abstractions;
using Modules.GraphProcessing.Algorithms;
using Modules.GraphProcessing.Normalization;
using Modules.GraphProcessing.Snapshots;
using UniversalWorkflowFramework.Core.Interfaces;
using UniversalWorkflowFramework.Core.Models;

namespace UniversalWorkflowFramework.Core.Graph
{
    /// <summary>
    /// 引用图引擎：组合 GraphProcessing，为 UR 暴露去重/快照/环/最短路径能力（消费方零改动，只增不改）。
    /// </summary>
    public sealed class ReferenceGraphEngine : IDisposable
    {
        private readonly IUniversalReferenceManager _manager;
        private readonly ReferenceGraphAdapter _adapter = new();

        public ReferenceGraphEngine(IUniversalReferenceManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>去重/完整性/孤立分析（对接 NormalizationExtensions.Deduplicate，只读分析不改源数据）。</summary>
        public async Task<NormalizationResult> DeduplicateReferencesAsync(NormalizationConfig? config = null)
        {
            using var graph = await BuildGraphAsync().ConfigureAwait(false);
            return graph.Deduplicate(config);
        }

        /// <summary>创建当前引用图快照。</summary>
        public async Task<GraphSnapshot<Resource, object>> CreateSnapshotAsync()
        {
            using var graph = await BuildGraphAsync().ConfigureAwait(false);
            return GraphSnapshot<Resource, object>.CreateFrom(graph, "ur-reference-graph");
        }

        /// <summary>比对两份引用图快照差异。</summary>
        public Task<GraphDiff<Resource, object>> CompareSnapshotsAsync(
            GraphSnapshot<Resource, object> oldSnapshot,
            GraphSnapshot<Resource, object> newSnapshot)
        {
            if (oldSnapshot == null) throw new ArgumentNullException(nameof(oldSnapshot));
            if (newSnapshot == null) throw new ArgumentNullException(nameof(newSnapshot));
            return Task.FromResult(GraphDiff<Resource, object>.Calculate(oldSnapshot, newSnapshot));
        }

        /// <summary>检测引用图环路径（BFS 无关，DFS 着色）。</summary>
        public async Task<IReadOnlyList<IReadOnlyList<string>>> FindCyclesAsync()
        {
            using var graph = await BuildGraphAsync().ConfigureAwait(false);
            var detector = new DefaultCycleDetector<string>();
            return detector.FindCycles(
                graph.GetAllNodeKeys().ToList(),
                key => graph.GetOutgoingEdges(key).Select(e => e.Target.ToString() ?? string.Empty));
        }

        /// <summary>BFS 最短路径。</summary>
        public async Task<IReadOnlyList<string>> FindShortestPathAsync(string startKey, string endKey)
        {
            using var graph = await BuildGraphAsync().ConfigureAwait(false);
            var finder = new DefaultShortestPathFinder<string>();
            return finder.FindShortestPath(
                startKey,
                endKey,
                key => graph.GetOutgoingEdges(key).Select(e => e.Target.ToString() ?? string.Empty));
        }

        private async Task<ICoreGraph<string, Resource, object>> BuildGraphAsync()
        {
            var resources = await _manager.GetAllResourcesAsync().ConfigureAwait(false) ?? new List<Resource>();

            var edges = new List<ReferenceEdge>();
            var seen = new HashSet<string>();
            foreach (var resource in resources)
            {
                var refs = await _manager.GetReferencesAsync(resource.Identifier, includeIncoming: false).ConfigureAwait(false);
                foreach (var (source, target, type, _location, description, strength, _isIncoming) in refs)
                {
                    var sourceKey = source.GetUniqueKey();
                    var targetKey = target.GetUniqueKey();
                    if (!seen.Add($"{sourceKey}->{targetKey}")) continue;

                    edges.Add(new ReferenceEdge
                    {
                        SourceKey = sourceKey,
                        TargetKey = targetKey,
                        ReferenceType = type.ToString(),
                        Description = description,
                        Strength = strength,
                        Data = new
                        {
                            SourceType = source.ResourceType,
                            SourceId = source.Id,
                            TargetType = target.ResourceType,
                            TargetId = target.Id,
                            ReferenceType = type.ToString(),
                            Description = description,
                            Strength = strength
                        }
                    });
                }
            }

            return _adapter.ToCoreGraph(resources, edges);
        }

        public void Dispose()
        {
        }
    }
}
