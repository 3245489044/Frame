#region USINGS
using GraphFoundation;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using GraphFramework;
using GraphFramework.Pipeline;
using Modules.Pipeline;
#endregion

namespace WorkFlowMethod.GraphFramework.Pipeline
{
    public class GraphCloneStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public string Name => "GraphClone";

        public IGraph<TKey, TNodeData, TEdgeData> Execute(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            Modules.Pipeline.IPipelineContext? context = null)
        {
            var clone = graph.Clone();
            return clone;
        }
    }

    public class GraphDeduplicateStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly Modules.GraphProcessing.Normalization.INormalizationConfig? _config;

        public string Name => "GraphDeduplicate";

        public GraphDeduplicateStep(Modules.GraphProcessing.Normalization.INormalizationConfig? config = null)
        {
            _config = config;
        }

        public IGraph<TKey, TNodeData, TEdgeData> Execute(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            Modules.Pipeline.IPipelineContext? context = null)
        {
            if (graph is ICoreGraph<TKey, TNodeData, TEdgeData> coreGraph)
            {
                var result = coreGraph.Deduplicate(_config as Modules.GraphProcessing.Normalization.NormalizationConfig);

                if (context != null)
                {
                    context.SharedData["Deduplicate_Success"] = result.IsSuccess;
                    if (result.MergedDuplicateEdges > 0)
                        context.SharedData["Deduplicate_MergedEdges"] = result.MergedDuplicateEdges;
                }
            }
            return graph;
        }
    }

    public class GraphValidateStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        public string Name => "GraphValidate";

        public IGraph<TKey, TNodeData, TEdgeData> Execute(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            Modules.Pipeline.IPipelineContext? context = null)
        {
            if (graph is IReadOnlyGraph<TKey, TNodeData, TEdgeData> readOnlyGraph)
            {
                var result = GraphFrameworkUsage.ValidateGraph(readOnlyGraph);

                if (!result.IsValid && !string.IsNullOrEmpty(result.ErrorMessage))
                    throw new InvalidOperationException(
                        $"Graph validation failed: {result.ErrorMessage}");
            }
            return graph;
        }
    }

    public class GraphAnalyzeStep<TKey, TNodeData, TEdgeData> : IGraphPipelineStep<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
        where TEdgeData : class
    {
        public string Name => "GraphAnalyze";

        public IGraph<TKey, TNodeData, TEdgeData> Execute(
            IGraph<TKey, TNodeData, TEdgeData> graph,
            Modules.Pipeline.IPipelineContext? context = null)
        {
            if (graph is IReadOnlyGraph<TKey, TNodeData, TEdgeData> readOnlyGraph)
            {
                var statsResult = GraphFrameworkUsage.AnalyzeGraph(readOnlyGraph);

                if (context != null)
                {
                    context.SharedData["LastGraphStats"] = statsResult;

                    var hasCycle = GraphFrameworkUsage.HasCycle(readOnlyGraph);
                    context.SharedData["LastTopologyResult"] = new { HasCycle = hasCycle };
                }
            }
            return graph;
        }
    }
}
