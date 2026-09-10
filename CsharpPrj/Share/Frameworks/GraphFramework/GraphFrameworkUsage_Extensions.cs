
#nullable enable

using System;
using System.Collections.Generic;
using GraphFoundation.Abstractions;
using GraphFramework.Models;
using GraphFramework.Directives;
using GraphFramework.Snapshots;
using GraphFramework.Analysis;
using GraphFramework.Execution;

namespace GraphFramework
{

    public static partial class GraphFrameworkUsage
    {
        public static Chain<TKey, TNode> CreateChain<TKey, TNode>(
            TKey? chainId = default,
            IEnumerable<TNode>? nodes = null,
            string? label = null)
            where TKey : notnull
            where TNode : class
            => new(chainId, nodes, label);

        public static ChainInfo<TKey, TNode> CreateChainInfo<TKey, TNode>(
            TKey? chainId = default,
            IEnumerable<TNode>? nodes = null,
            string? label = null)
            where TKey : notnull
            where TNode : class
            => new(chainId, nodes, label);

        public static NodeTemplate<TKey, TNode> CreateNodeTemplate<TKey, TNode>(
            string symbol,
            string? label = null,
            TNode? nodeData = null)
            where TKey : notnull
            where TNode : class
            => new() { Symbol = symbol, Label = label, NodeData = nodeData };

        public static EdgeTemplate<TKey> CreateEdgeTemplate<TKey>(
            TKey source, TKey target,
            bool isDirected = true, double weight = 1.0)
            where TKey : notnull
            => new() { Source = source, Target = target, IsDirected = isDirected, Weight = weight };
    }


    public static partial class GraphFrameworkUsage
    {
        public static IDirectiveApplier<TKey, TNode, TEdge> CreateDirectiveApplier<TKey, TNode, TEdge>()
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => new DefaultDirectiveApplier<TKey, TNode, TEdge>();

        public static InsertNodeDirective<TKey, TNode, TEdge> CreateInsertDirective<TKey, TNode, TEdge>(
            params NodeTemplate<TKey, TNode>[] nodes)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => new() { NodesToInsert = new List<NodeTemplate<TKey, TNode>>(nodes) };

        public static DeleteNodeDirective<TKey, TNode, TEdge> CreateDeleteDirective<TKey, TNode, TEdge>(
            TKey nodeKey)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => new() { NodeKey = nodeKey };

        public static ReplaceNodeDirective<TKey, TNode, TEdge> CreateReplaceDirective<TKey, TNode, TEdge>(
            TKey oldNodeKey, NodeTemplate<TKey, TNode> newNode)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => new() { OldNodeKey = oldNodeKey, NewNode = newNode };

        public static CompositeDirective<TKey, TNode, TEdge> CreateCompositeDirective<TKey, TNode, TEdge>(
            params GraphUpdateDirective<TKey, TNode, TEdge>[] directives)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => new() { Directives = new List<GraphUpdateDirective<TKey, TNode, TEdge>>(directives) };

        public static DirectiveResult ApplyDirective<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph,
            GraphUpdateDirective<TKey, TNode, TEdge> directive)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var applier = new DefaultDirectiveApplier<TKey, TNode, TEdge>();
            return applier.Apply(graph, directive);
        }

        public static List<DirectiveResult> ApplyDirectives<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph,
            params GraphUpdateDirective<TKey, TNode, TEdge>[] directives)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var applier = new DefaultDirectiveApplier<TKey, TNode, TEdge>();
            return applier.ApplyBatch(graph, directives);
        }
    }


    public static partial class GraphFrameworkUsage
    {
        public static GraphSnapshot<TKey, TNode, TEdge> CreateSnapshot<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph,
            string? graphId = null, int version = 1)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => GraphSnapshot<TKey, TNode, TEdge>.CreateFrom(graph, graphId, version);

        public static GraphDiff<TKey, TNode, TEdge> DiffSnapshots<TKey, TNode, TEdge>(
            GraphSnapshot<TKey, TNode, TEdge>? before,
            GraphSnapshot<TKey, TNode, TEdge>? after)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => GraphDiff<TKey, TNode, TEdge>.Compare(before, after);

        public static bool ValidateSnapshot<TKey, TNode, TEdge>(
            this GraphSnapshot<TKey, TNode, TEdge> snapshot,
            out IReadOnlyList<string> errors)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => snapshot.Validate(out errors);
    }


    public static partial class GraphFrameworkUsage
    {
        public static GraphStatistics<TKey> AnalyzeGraph<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var analyzer = new DefaultGraphAnalyzer<TKey, TNode, TEdge>();
            return analyzer.Analyze(graph);
        }

        public static Result<List<TKey>> TopologicalSort<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var analyzer = new DefaultGraphAnalyzer<TKey, TNode, TEdge>();
            return analyzer.TopologicalSort(graph);
        }

        public static List<List<TKey>> FindAllPaths<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph,
            TKey start, TKey end)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var analyzer = new DefaultGraphAnalyzer<TKey, TNode, TEdge>();
            return analyzer.FindAllPaths(graph, start, end);
        }

        public static List<Chain<TKey, TNode>> FindChains<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var analyzer = new DefaultGraphAnalyzer<TKey, TNode, TEdge>();
            return analyzer.FindChains(graph);
        }

        public static bool HasCycle<TKey, TNode, TEdge>(
            this IGraph<TKey, TNode, TEdge> graph,
            out List<TKey>? cycleNodes)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var analyzer = new DefaultGraphAnalyzer<TKey, TNode, TEdge>();
            return analyzer.HasCycle(graph, out cycleNodes);
        }
    }


    public static partial class GraphFrameworkUsage
    {
        public static IChainExecutor<TKey, TNode, TEdge, TContext> CreateChainExecutor<TKey, TNode, TEdge, TContext>(
            string name,
            INodeProcessor<TKey, TNode, TEdge, TContext> nodeProcessor)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            where TContext : class
            => new DefaultChainExecutor<TKey, TNode, TEdge, TContext>(name, nodeProcessor);

        public static Result<Chain<TKey, TNode>> ExecuteChain<TKey, TNode, TEdge, TContext>(
            this Chain<TKey, TNode> chain,
            IGraph<TKey, TNode, TEdge> graph,
            IChainExecutor<TKey, TNode, TEdge, TContext> executor,
            TContext context)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            where TContext : class
            => executor.ExecuteChain(chain, graph, context);
    }
}
