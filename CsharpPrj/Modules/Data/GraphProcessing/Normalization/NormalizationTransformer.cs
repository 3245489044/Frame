
#nullable enable

using Modules.Transformers;
using GraphFoundation.Abstractions;
using GraphFoundation.Topology.Transformers;

namespace Modules.GraphProcessing.Normalization
{
    public class NormalizationResult
    {
        public int RemovedIsolatedNodes { get; set; }

        public int MergedDuplicateEdges { get; set; }

        public int NormalizedIdentifiers { get; set; }

        public int ValidationIssues { get; set; }

        public List<string> Log { get; } = new();

        public bool IsSuccess => true;

        public void Merge(NormalizationResult other)
        {
            RemovedIsolatedNodes += other.RemovedIsolatedNodes;
            MergedDuplicateEdges += other.MergedDuplicateEdges;
            NormalizedIdentifiers += other.NormalizedIdentifiers;
            ValidationIssues += other.ValidationIssues;
            Log.AddRange(other.Log);
        }

        public override string ToString()
            => $"NormalizationResult: IsolatedRemoved={RemovedIsolatedNodes}, " +
               $"DuplicatesMerged={MergedDuplicateEdges}, " +
               $"IdentifiersNormalized={NormalizedIdentifiers}, " +
               $"ValidationIssues={ValidationIssues}";
    }


    public interface INormalizationSubStep<TKey, TNodeData, TEdgeData> : ITransformer<ICoreGraph<TKey, TNodeData, TEdgeData>, NormalizationResult>
        where TKey : notnull
        where TNodeData : class
    {
        bool IsEnabled { get; }
    }


    public abstract class NormalizationSubStepBase<TKey, TNodeData, TEdgeData> : INormalizationSubStep<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        protected readonly INormalizationConfig Config;

        public abstract string Name { get; }
        public abstract bool IsEnabled { get; }

        protected NormalizationSubStepBase(INormalizationConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public abstract NormalizationResult Transform(ICoreGraph<TKey, TNodeData, TEdgeData> graph);

        protected void Log(NormalizationResult result, string message)
        {
            if (Config.VerboseLogging)
                result.Log.Add(message);
        }
    }


    public class MergeDuplicateEdgesTransformer<TKey, TNodeData, TEdgeData> : NormalizationSubStepBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public override string Name => "MergeDuplicateEdges";
        public override bool IsEnabled => Config.MergeDuplicateEdges;

        public MergeDuplicateEdgesTransformer(INormalizationConfig config) : base(config) { }

        public override NormalizationResult Transform(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var result = new NormalizationResult();

            var connections = new List<Connection>();
            foreach (var edgeId in graph.GetAllEdgeIds())
            {
                var source = graph.GetEdgeSource(edgeId);
                var target = graph.GetEdgeTarget(edgeId);
                var isDirected = graph.GetEdgeIsDirected(edgeId);
                var weight = graph.GetEdgeWeight(edgeId);
                connections.Add(new Connection(
                    edgeId,
                    source.ToString() ?? edgeId,
                    target.ToString() ?? edgeId,
                    isDirected,
                    weight));
            }

            if (connections.Count == 0)
            {
                Log(result, "  No edges to deduplicate.");
                return result;
            }

            var matchStrategy = Config.DedupeMatchStrategy
                ?? new SourceTargetDirectionMatch().AsObjectMatch();
            var reduceStrategy = Config.DedupeReduceStrategy
                ?? new KeepFirstReduce();

            var reducer = new GroupReducer<Connection>(
                "Deduplication",
                matchStrategy,
                reduceStrategy);
            var deduped = reducer.Transform(connections).ToList();

            result.MergedDuplicateEdges = connections.Count - deduped.Count;

            var dedupedIds = new HashSet<string>(deduped.Select(c => c.Id));
            foreach (var conn in connections)
            {
                if (!dedupedIds.Contains(conn.Id))
                {
                    graph.RemoveEdge(conn.Id);
                    Log(result, $"  Merged duplicate edge: {conn.Id}");
                }
            }


            if (result.MergedDuplicateEdges == 0)
                Log(result, "  No duplicate edges found.");
            else
                Log(result, $"  Merged {result.MergedDuplicateEdges} duplicate edges.");

            return result;
        }
    }


    public class RemoveIsolatedNodesTransformer<TKey, TNodeData, TEdgeData> : NormalizationSubStepBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public override string Name => "RemoveIsolatedNodes";
        public override bool IsEnabled => Config.RemoveIsolatedNodes;

        public RemoveIsolatedNodesTransformer(INormalizationConfig config) : base(config) { }

        public override NormalizationResult Transform(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var result = new NormalizationResult();

            var isolated = new List<TKey>();
            foreach (var key in graph.GetAllNodeKeys())
            {
                if (graph.GetOutgoingEdges(key).Count == 0 &&
                    graph.GetIncomingEdges(key).Count == 0)
                {
                    isolated.Add(key);
                }
            }

            foreach (var key in isolated)
            {
                graph.RemoveNode(key);
                result.RemovedIsolatedNodes++;
                Log(result, $"  Removed isolated node: {key}");
            }

            if (result.RemovedIsolatedNodes == 0)
                Log(result, "  No isolated nodes found.");

            return result;
        }
    }


    public class NormalizeIdentifiersTransformer<TKey, TNodeData, TEdgeData> : NormalizationSubStepBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public override string Name => "NormalizeIdentifiers";
        public override bool IsEnabled => Config.NormalizeIdentifiers;

        public NormalizeIdentifiersTransformer(INormalizationConfig config) : base(config) { }

        public override NormalizationResult Transform(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var result = new NormalizationResult();

            if (typeof(TKey) != typeof(string))
            {
                Log(result, "  Identifier normalization requires string keys (TKey = string). Skipping.");
                return result;
            }

            var extractor = new IdentifierExtractor();
            var renameMap = new Dictionary<string, string>();

            foreach (var key in graph.GetAllNodeKeys())
            {
                var keyStr = key.ToString() ?? string.Empty;
                var normalized = extractor.Extract(keyStr);
                if (!string.Equals(keyStr, normalized, StringComparison.Ordinal))
                {
                    renameMap[keyStr] = normalized;
                }
            }

            foreach (var (original, normalized) in renameMap)
            {
                var originalKey = (TKey)Convert.ChangeType(original, typeof(TKey))!;
                var normalizedKey = (TKey)Convert.ChangeType(normalized, typeof(TKey))!;


                var nodeData = graph.GetNodeData(originalKey);
                var nodeSymbol = graph.GetNodeSymbol(originalKey);

                var outgoingEdges = graph.GetOutgoingEdges(originalKey);
                var incomingEdges = graph.GetIncomingEdges(originalKey);

                graph.AddNode(normalizedKey, nodeData, nodeSymbol);

                foreach (var (src, target, edgeId, isDirected, weight, data) in outgoingEdges)
                {
                    graph.RemoveEdge(edgeId);
                    graph.AddEdge(edgeId, normalizedKey, target, isDirected, data, weight);
                }

                foreach (var (source, tgt, edgeId, isDirected, weight, data) in incomingEdges)
                {
                    graph.RemoveEdge(edgeId);
                    graph.AddEdge(edgeId, source, normalizedKey, isDirected, data, weight);
                }

                graph.RemoveNode(originalKey);

                result.NormalizedIdentifiers++;
                Log(result, $"  Normalized identifier: '{original}' -> '{normalized}'");
            }

            if (result.NormalizedIdentifiers == 0)
                Log(result, "  No identifiers to normalize.");

            return result;
        }
    }


    public class ValidateIntegrityTransformer<TKey, TNodeData, TEdgeData> : NormalizationSubStepBase<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public override string Name => "ValidateIntegrity";
        public override bool IsEnabled => Config.ValidateIntegrity;

        public ValidateIntegrityTransformer(INormalizationConfig config) : base(config) { }

        public override NormalizationResult Transform(ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            var result = new NormalizationResult();

            foreach (var edgeId in graph.GetAllEdgeIds())
            {
                var source = graph.GetEdgeSource(edgeId);
                var target = graph.GetEdgeTarget(edgeId);

                if (!graph.ContainsNode(source))
                {
                    result.ValidationIssues++;
                    Log(result, $"  Validation issue: Edge {edgeId} references missing source node {source}");
                }

                if (!graph.ContainsNode(target))
                {
                    result.ValidationIssues++;
                    Log(result, $"  Validation issue: Edge {edgeId} references missing target node {target}");
                }
            }

            if (result.ValidationIssues == 0)
                Log(result, "  Validation passed: all edges reference valid nodes.");

            return result;
        }
    }


    public class NormalizationTransformer
    {
        private readonly INormalizationConfig _config;

        public NormalizationTransformer(INormalizationConfig? config = null)
        {
            _config = config ?? new NormalizationConfig();
        }

        private List<INormalizationSubStep<TKey, TNodeData, TEdgeData>> CreateSubSteps<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
        {
            var steps = new List<INormalizationSubStep<TKey, TNodeData, TEdgeData>>();

            var mergeDuplicates = new MergeDuplicateEdgesTransformer<TKey, TNodeData, TEdgeData>(_config);
            steps.Add(mergeDuplicates);

            var removeIsolated = new RemoveIsolatedNodesTransformer<TKey, TNodeData, TEdgeData>(_config);
            steps.Add(removeIsolated);

            var normalizeIds = new NormalizeIdentifiersTransformer<TKey, TNodeData, TEdgeData>(_config);
            steps.Add(normalizeIds);

            var validate = new ValidateIntegrityTransformer<TKey, TNodeData, TEdgeData>(_config);
            steps.Add(validate);

            return steps;
        }

        public NormalizationResult Execute<TKey, TNodeData, TEdgeData>(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var result = new NormalizationResult();
            Log(result, "Starting normalization...");

            var steps = CreateSubSteps<TKey, TNodeData, TEdgeData>();

            foreach (var step in steps)
            {
                if (!step.IsEnabled)
                {
                    Log(result, $"  Skipping {step.Name} (disabled by config).");
                    continue;
                }

                var stepResult = step.Transform(graph);
                result.Merge(stepResult);
            }

            Log(result, "Normalization completed.");
            return result;
        }

        private void Log(NormalizationResult result, string message)
        {
            if (_config.VerboseLogging)
                result.Log.Add(message);
        }
    }
}
