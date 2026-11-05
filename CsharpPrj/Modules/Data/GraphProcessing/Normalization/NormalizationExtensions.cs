
#nullable enable

using GraphFoundation.Abstractions;
using Modules.GraphProcessing.Orchestration;
using Modules.GraphProcessing.Reference;
using Modules.GraphProcessing.Utilities;

namespace Modules.GraphProcessing.Normalization
{
    public static class NormalizationExtensions
    {

        public static NormalizationResult Normalize<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            INormalizationConfig? config = null)
            where TKey : notnull
            where TNodeData : class
        {
            var transformer = new NormalizationTransformer(config);
            return transformer.Execute(graph);
        }


        public static NormalizationResult Deduplicate<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            NormalizationConfig? config = null)
            where TKey : notnull
            where TNodeData : class
        {
            config ??= NormalizationConfig.CreateBasic();
            var transformer = new NormalizationTransformer(config);
            return transformer.Execute(graph);
        }

        public static NormalizationResult DeduplicateByWeight<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => graph.Deduplicate(NormalizationConfig.CreateWeightAware());

        public static NormalizationResult DeduplicateStrict<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => graph.Deduplicate(NormalizationConfig.CreateStrict());

        public static NormalizationResult DeduplicateUndirected<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            => graph.Deduplicate(NormalizationConfig.CreateUndirected());


        public static ChainResult<TKey> CreateChainFromTemplate<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            TNodeData template,
            int count,
            IKeyGenerator<TKey>? keyGenerator = null,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null)
            where TKey : notnull
            where TNodeData : class
        {
            var service = new OrchestrationService<TKey, TNodeData, TEdgeData>(
                graph, keyExtractor, setPosition, cloner);
            return service.CreateChainFromTemplate(template, count, keyGenerator);
        }

        public static ChainResult<TKey> ConnectChains<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IEnumerable<ChainResult<TKey>> chains,
            IEdgeManager<TKey, TNodeData, TEdgeData>? edgeManager = null)
            where TKey : notnull
            where TNodeData : class
        {
            edgeManager ??= new DefaultEdgeManager<TKey, TNodeData, TEdgeData>();
            var merger = new ChainMerger<TKey, TNodeData, TEdgeData>(edgeManager);
            return merger.Merge(chains, graph);
        }

        public static void RebuildSequentialRelations<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IList<TKey> nodeKeys,
            string chainId,
            IEdgeManager<TKey, TNodeData, TEdgeData>? edgeManager = null)
            where TKey : notnull
            where TNodeData : class
        {
            edgeManager ??= new DefaultEdgeManager<TKey, TNodeData, TEdgeData>();
            using var tx = graph.BeginTransaction();
            edgeManager.Rebuild(tx, nodeKeys, Enumerable.Empty<(TKey, TKey)>(), chainId, graph);
            tx.Commit();
        }


        public static TNodeData RewriteReferences<TKey, TNodeData>(
            this TNodeData data,
            IReadOnlyDictionary<TKey, TKey> idMap,
            IReadOnlyGraph<TKey, TNodeData, object> graph,
            CrossChainReferenceStrategy strategy = CrossChainReferenceStrategy.Rewrite)
            where TKey : notnull
            where TNodeData : class
        {
            var rewriter = new ReferenceRewriter<TKey, TNodeData>(idMap, strategy);
            return rewriter.Rewrite(data, graph);
        }

        public static T UpdateReferences<T, TKey, TNodeData>(
            this T obj,
            IReferenceResolver<TKey, TNodeData> resolver,
            IReadOnlyGraph<TKey, TNodeData, object> graph)
            where TKey : notnull
            where TNodeData : class
        {
            return DeepReferenceUpdaterUtility.UpdateReferences(obj, resolver, graph);
        }


        public static T DeepClone<T>(this T obj)
        {
            return DeepCopyUtility.DeepCopy(obj);
        }

        public static T TraverseGraph<T>(this T obj, Func<object, object> visitor)
        {
            return ObjectTraversalUtility.Traverse(obj, visitor);
        }
    }
}
