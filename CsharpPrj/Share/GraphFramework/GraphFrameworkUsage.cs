
#nullable enable

using ECS.Core;
using TConfig.ECS;
using GraphFoundation.Abstractions;
using GraphFramework.Pipeline;
using GraphFramework.Ecs.Pipeline;
using GraphFramework.Ecs.Systems;
using DisposeGraphProcessing = Modules.GraphProcessing.Orchestration;
using DisposeGraphProcessingRef = Modules.GraphProcessing.Reference;
using DisposeNormalization = Modules.GraphProcessing.Normalization;
using DisposeSnapshots = Modules.GraphProcessing.Snapshots;
using DisposeOperations = Modules.GraphProcessing.Operations;
using DisposeGraph = Modules.GraphProcessing.Infrastructure.Graph;
using Modules.GraphProcessing;
using DisposeNormalizationExtensions = Modules.GraphProcessing.Normalization.NormalizationExtensions;
using TConfig.ContextFrame.Abstractions;

namespace GraphFramework
{
    public static partial class GraphFrameworkUsage
    {
        #region 1. 图构建

        public static ICoreGraph<TKey, TNodeData, TEdgeData> CreateGraph<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
        {
            return new GraphFoundation.Core.CoreGraph<TKey, TNodeData, TEdgeData>();
        }

        #endregion

        #region 2. 图变换
                                        #endregion

        #region 3. 归一化与去重

        public static DisposeNormalization.NormalizationResult Normalize<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            DisposeNormalization.INormalizationConfig? config = null)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.Normalize(graph, config);
        }

        public static DisposeNormalization.NormalizationResult Deduplicate<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            DisposeNormalization.NormalizationConfig? config = null)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.Deduplicate(graph, config);
        }

        public static DisposeNormalization.NormalizationResult DeduplicateByWeight<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.DeduplicateByWeight<TKey, TNodeData, TEdgeData>(graph);
        }

        public static DisposeNormalization.NormalizationResult DeduplicateStrict<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.DeduplicateStrict<TKey, TNodeData, TEdgeData>(graph);
        }

        public static DisposeNormalization.NormalizationResult DeduplicateUndirected<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.DeduplicateUndirected<TKey, TNodeData, TEdgeData>(graph);
        }

        public static DisposeNormalization.StringDedupeResult SmartDeduplicate(
            IReadOnlyList<string> items,
            double threshold = 0.8)
        {
            var engine = new DisposeNormalization.SmartDeduplicationEngine(threshold);
            return engine.Execute(items);
        }

        #endregion

        #region 4. 链编排

        public static DisposeGraphProcessing.ChainResult<TKey> CreateChainFromTemplate<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            TNodeData template,
            int count,
            DisposeGraphProcessing.IKeyGenerator<TKey>? keyGenerator = null,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.CreateChainFromTemplate(graph, template, count,
                keyGenerator, keyExtractor, setPosition, cloner);
        }

        public static DisposeGraphProcessing.OrchestrationResult<TKey> CreateMultipleChains<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            TNodeData template,
            int chainCount,
            int nodesPerChain,
            DisposeGraphProcessing.IKeyGenerator<TKey>? keyGenerator = null,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null)
            where TKey : notnull
            where TNodeData : class
        {
            var service = new DisposeGraphProcessing.OrchestrationService<TKey, TNodeData, TEdgeData>(
                graph, keyExtractor, setPosition, cloner);
            return service.CreateMultipleChains(template, chainCount, nodesPerChain, keyGenerator);
        }

        public static DisposeGraphProcessing.ChainResult<TKey> ConnectChains<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IEnumerable<DisposeGraphProcessing.ChainResult<TKey>> chains)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.ConnectChains(graph, chains);
        }

        public static void RebuildSequentialRelations<TKey, TNodeData, TEdgeData>(
            this ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IList<TKey> nodeKeys,
            string chainId)
            where TKey : notnull
            where TNodeData : class
        {
            DisposeNormalizationExtensions.RebuildSequentialRelations(graph, nodeKeys, chainId);
        }

        #endregion

        #region 5. 引用处理

        public static TNodeData RewriteReferences<TKey, TNodeData>(
            this TNodeData data,
            IReadOnlyDictionary<TKey, TKey> idMap,
            IReadOnlyGraph<TKey, TNodeData, object> graph,
            DisposeGraphProcessingRef.CrossChainReferenceStrategy strategy = DisposeGraphProcessingRef.CrossChainReferenceStrategy.Rewrite)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.RewriteReferences(data, idMap, graph, strategy);
        }

        public static T UpdateReferences<T, TKey, TNodeData>(
            this T obj,
            DisposeGraphProcessingRef.IReferenceResolver<TKey, TNodeData> resolver,
            IReadOnlyGraph<TKey, TNodeData, object> graph)
            where TKey : notnull
            where TNodeData : class
        {
            return DisposeNormalizationExtensions.UpdateReferences(obj, resolver, graph);
        }

        public static DisposeGraphProcessingRef.ReferenceRewriter<TKey, TNodeData> CreateReferenceRewriter<TKey, TNodeData>(
            IReadOnlyDictionary<TKey, TKey> idMap,
            DisposeGraphProcessingRef.CrossChainReferenceStrategy strategy = DisposeGraphProcessingRef.CrossChainReferenceStrategy.Rewrite)
            where TKey : notnull
            where TNodeData : class
        {
            var service = new DisposeGraphProcessingRef.ReferenceService<TKey, TNodeData>();
            return service.CreateRewriter(idMap, strategy);
        }

        public static DisposeGraphProcessingRef.DefaultReferenceResolver<TKey, TNodeData> CreateReferenceResolver<TKey, TNodeData>(
            Func<object, IReadOnlyGraph<TKey, TNodeData, object>, object>? resolveFunc = null)
            where TKey : notnull
            where TNodeData : class
        {
            var service = new DisposeGraphProcessingRef.ReferenceService<TKey, TNodeData>();
            return service.CreateResolver(resolveFunc);
        }

        #endregion


        #region 7. 管线执行


        public static GraphPipeline<TKey, TNodeData, TEdgeData> CreateGraphPipeline<TKey, TNodeData, TEdgeData>(
            string name = "default")
            where TKey : notnull
            where TNodeData : class
            => new GraphPipeline<TKey, TNodeData, TEdgeData>(name);

        public static GraphPipeline<TKey, TNodeData, TEdgeData> CreateFullGraphPipeline<TKey, TNodeData, TEdgeData>(
            string name = "full")
            where TKey : notnull
            where TNodeData : class
            => GraphPipeline<TKey, TNodeData, TEdgeData>.CreateFull(name);

        public static GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData> CreateOrchestrator<TKey, TNodeData, TEdgeData>(
            string name = "GraphOrchestrator")
            where TKey : notnull
            where TNodeData : class
            => new GraphPipelineOrchestrator<TKey, TNodeData, TEdgeData>(name);

        #endregion


        #region 9. 系统执行

        private static (EcsWorld World, TSystem System) CreateWorldAndSystem<TSystem>(
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            Func<EcsWorld, TSystem> systemFactory)
            where TSystem : SystemBase
        {
            var world = new EcsWorld(ecsStorage, storageRegistry);
            var system = systemFactory(world);
            system.Initialize(world.EntityManager);
            return (world, system);
        }

        private static TComponent ExtractComponentOrThrow<TComponent>(EcsWorld world)
            where TComponent : struct, IComponent
        {
            foreach (var entity in world.EntityManager.GetAllEntities())
            {
                if (world.EntityManager.TryGetComponent<TComponent>(entity, out var component))
                {
                    world.Dispose();
                    return component;
                }
            }

            world.Dispose();
            throw new InvalidOperationException(
                $"No {typeof(TComponent).Name} found after system execution.");
        }

        public static GraphStatsComponent RunStatistics<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry)
            where TKey : notnull
            where TNodeData : class
        {
            var (world, system) = CreateWorldAndSystem<StatisticsSystem<TKey, TNodeData, TEdgeData>>(
                ecsStorage, storageRegistry,
                w => new StatisticsSystem<TKey, TNodeData, TEdgeData>(graph));
            system.Update(0);
            return ExtractComponentOrThrow<GraphStatsComponent>(world);
        }

        public static ValidationResultComponent RunValidation<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry)
            where TKey : notnull
            where TNodeData : class
        {
            var (world, system) = CreateWorldAndSystem<ValidationSystem<TKey, TNodeData, TEdgeData>>(
                ecsStorage, storageRegistry,
                w => new ValidationSystem<TKey, TNodeData, TEdgeData>(graph));
            system.Update(0);
            return ExtractComponentOrThrow<ValidationResultComponent>(world);
        }

        public static TopologyResultComponent RunTopology<TKey, TNodeData, TEdgeData>(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            Func<TKey, string>? keyToString = null)
            where TKey : notnull
            where TNodeData : class
        {
            var (world, system) = CreateWorldAndSystem<TopologySystem<TKey, TNodeData, TEdgeData>>(
                ecsStorage, storageRegistry,
                w => new TopologySystem<TKey, TNodeData, TEdgeData>(graph, keyToString));
            system.Update(0);
            return ExtractComponentOrThrow<TopologyResultComponent>(world);
        }

        public static ChainProcessingResultComponent RunChainProcessing<TKey, TNodeData, TEdgeData>(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            Func<string, TKey> keyFactory,
            Func<TKey, TNodeData> nodeDataFactory,
            Func<TKey, TKey, TEdgeData> edgeDataFactory)
            where TKey : notnull
            where TNodeData : class
        {
            var (world, system) = CreateWorldAndSystem<ChainSystem<TKey, TNodeData, TEdgeData>>(
                ecsStorage, storageRegistry,
                w => new ChainSystem<TKey, TNodeData, TEdgeData>(
                    graph, keyFactory, nodeDataFactory, edgeDataFactory));

            var allNodeKeys = graph.GetAllNodeKeys();
            if (allNodeKeys.Count > 0)
            {
                var chainEntity = world.EntityManager.CreateEntity();
                world.EntityManager.AddComponent(chainEntity,
                    new ChainDataComponent<TKey>("default", allNodeKeys.ToArray()));
            }

            system.Update(0);
            return ExtractComponentOrThrow<ChainProcessingResultComponent>(world);
        }

        public static TransformationResultComponent RunTransformation<TKey, TNodeData, TEdgeData>(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            Func<TKey, TNodeData> nodeDataFactory,
            Func<TKey, TKey, TEdgeData> edgeDataFactory,
            Func<string, TKey>? keyFactory = null,
            string operation = "reverse",
            string[]? parameters = null)
            where TKey : notnull
            where TNodeData : class
        {
            var (world, system) = CreateWorldAndSystem<TransformationSystem<TKey, TNodeData, TEdgeData>>(
                ecsStorage, storageRegistry,
                w => new TransformationSystem<TKey, TNodeData, TEdgeData>(
                    graph, nodeDataFactory, edgeDataFactory, keyFactory));

            var requestEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponent(requestEntity,
                new TransformationRequestComponent<TKey, TEdgeData>(operation, parameters));

            system.Update(0);

            if (world.EntityManager.TryGetComponent<TransformationResultComponent>(requestEntity, out var result))
            {
                world.Dispose();
                return result;
            }

            world.Dispose();
            return default;
        }

        public static OrchestrationResultComponent RunOrchestration<TKey, TNodeData, TEdgeData>(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IECSStorage ecsStorage,
            IStorageRegistry storageRegistry,
            TNodeData template,
            int chainCount,
            int nodesPerChain,
            DisposeGraphProcessing.IKeyGenerator<TKey>? keyGenerator = null,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null)
            where TKey : notnull
            where TNodeData : class
        {
            var (world, system) = CreateWorldAndSystem<OrchestrationSystem<TKey, TNodeData, TEdgeData>>(
                ecsStorage, storageRegistry,
                w => new OrchestrationSystem<TKey, TNodeData, TEdgeData>(
                    graph, template, chainCount, nodesPerChain,
                    keyGenerator, keyExtractor, setPosition, cloner));
            system.Update(0);

            var result = OrchestrationSystem<TKey, TNodeData, TEdgeData>.GetResult(world);
            world.Dispose();
            return result ?? default;
        }

        #endregion

        #region 10. 工具方法

        public static T DeepClone<T>(this T obj)
        {
            return DisposeNormalizationExtensions.DeepClone(obj);
        }

        public static T TraverseGraph<T>(this T obj, Func<object, object> visitor)
        {
            return DisposeNormalizationExtensions.TraverseGraph(obj, visitor);
        }

        public static string ExtractIdentifier(
            string input,
            string separator = "_",
            bool preserveCase = false)
        {
            var extractor = new DisposeNormalization.IdentifierExtractor(separator, preserveCase);
            return extractor.Extract(input);
        }

        public static bool AreIdentifiersMatching(string a, string b)
        {
            var extractor = new DisposeNormalization.IdentifierExtractor();
            return extractor.AreMatching(a, b);
        }

        public static string GenerateUniqueId(
            string baseId,
            int chainIndex,
            Func<string, bool> isNameTaken,
            string pattern = "{0}_chain{1}_dup{2}",
            int maxAttempts = 100)
        {
            var generator = new DisposeNormalization.DefaultUniqueIdGenerator(pattern, maxAttempts);
            return generator.Generate(baseId, chainIndex, isNameTaken);
        }

        public static DisposeNormalization.INormalizationContext CreateNormalizationContext()
        {
            return new DisposeNormalization.DefaultNormalizationContext();
        }

        public static IDisposable BeginNormalizationContextScope(
            DisposeNormalization.INormalizationContext? parent = null)
        {
            return DisposeNormalization.DefaultNormalizationContext.BeginScope(parent);
        }

        public static DisposeGraphProcessing.IKeyGenerator<TKey> CreateDefaultKeyGenerator<TKey>(
            Func<int, TKey>? converter = null)
        {
            return DisposeGraphProcessing.OrchestrationService<TKey, object, object>.CreateDefaultKeyGenerator(converter);
        }

        public static DisposeGraphProcessing.INodeGenerator<TNodeData> CreateDefaultNodeGenerator<TNodeData>(
            Func<TNodeData, TNodeData>? cloner = null)
            where TNodeData : class
        {
            return DisposeGraphProcessing.OrchestrationService<object, TNodeData, object>.CreateDefaultNodeGenerator(cloner);
        }

        public static DisposeGraphProcessing.IEdgeManager<TKey, TNodeData, TEdgeData> CreateDefaultEdgeManager<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
        {
            return new DisposeGraphProcessing.DefaultEdgeManager<TKey, TNodeData, TEdgeData>();
        }

        #endregion

        #region 11. 快照、分析、验证

        public static DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> CreateSnapshot<TNodeData, TEdgeData>(
            IReadOnlyGraph<string, TNodeData, TEdgeData> graph,
            string? graphId = null,
            int version = 1)
            where TNodeData : class
            where TEdgeData : class
        {
            return DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData>.CreateFrom(graph, graphId, version);
        }

        public static DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> CreateSnapshotFromData<TNodeData, TEdgeData>(
            string graphId,
            int version,
            IReadOnlyDictionary<string, GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> nodes,
            IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> edges)
            where TNodeData : class
            where TEdgeData : class
        {
            return DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData>.CreateFrom(graphId, version, nodes, edges);
        }

        public static DisposeSnapshots.GraphDiff<TNodeData, TEdgeData> CalculateDiff<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> oldSnapshot,
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> newSnapshot)
            where TNodeData : class
            where TEdgeData : class
        {
            return DisposeSnapshots.GraphDiff<TNodeData, TEdgeData>.Calculate(oldSnapshot, newSnapshot);
        }

        public static string DiffSummary<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphDiff<TNodeData, TEdgeData> diff)
            where TNodeData : class
            where TEdgeData : class
        {
            return diff.ToString();
        }

        public static GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>? GetEntryNode<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetEntryNode();
        }

        public static IReadOnlyList<GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> GetEntryNodes<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetEntryNodes();
        }

        public static IReadOnlyList<string> GetNextNodeKeys<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            string nodeKey)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetNextNodeKeys(nodeKey);
        }

        public static IReadOnlyList<GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> GetNextNodes<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            string nodeKey)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetNextNodes(nodeKey);
        }

        public static IReadOnlyList<string> GetPreviousNodeKeys<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            string nodeKey)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetPreviousNodeKeys(nodeKey);
        }

        public static IReadOnlyList<GraphFoundation.Snapshots.NodeDefinition<string, TNodeData>> GetPreviousNodes<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            string nodeKey)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetPreviousNodes(nodeKey);
        }

        public static IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> GetSnapshotIncomingEdges<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            string nodeKey)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetIncomingEdges(nodeKey);
        }

        public static IReadOnlyList<GraphFoundation.Snapshots.EdgeDefinition<string>> GetSnapshotOutgoingEdges<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            string nodeKey)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.GetOutgoingEdges(nodeKey);
        }

        public static bool ValidateSnapshotTopology<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot,
            out IReadOnlyList<string> errors)
            where TNodeData : class
            where TEdgeData : class
        {
            return snapshot.Validate(out errors);
        }

        public static DisposeOperations.IGraphAnalyzer<TKey, TNodeData, TEdgeData> CreateAnalyzer<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
        {
            return new DisposeOperations.DefaultGraphAnalyzer<TKey, TNodeData, TEdgeData>();
        }

        public static DisposeOperations.GraphStatistics AnalyzeGraph<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
        {
            var analyzer = new DisposeOperations.DefaultGraphAnalyzer<TKey, TNodeData, TEdgeData>();
            return analyzer.Analyze(graph);
        }

        public static DisposeOperations.IGraphValidator<TKey, TNodeData, TEdgeData> CreateValidator<TKey, TNodeData, TEdgeData>()
            where TKey : notnull
            where TNodeData : class
            where TEdgeData : class
        {
            return new DisposeOperations.DefaultGraphValidator<TKey, TNodeData, TEdgeData>();
        }

        public static DisposeOperations.ValidationResult ValidateGraph<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            where TEdgeData : class
        {
            var validator = new DisposeOperations.DefaultGraphValidator<TKey, TNodeData, TEdgeData>();
            return validator.Validate(graph);
        }

        public static DisposeOperations.ValidationResult ValidateSnapshot<TNodeData, TEdgeData>(
            DisposeSnapshots.GraphSnapshot<TNodeData, TEdgeData> snapshot)
            where TNodeData : class
            where TEdgeData : class
        {
            var validator = new DisposeOperations.DefaultGraphValidator<string, TNodeData, TEdgeData>();
            return validator.ValidateSnapshot(snapshot);
        }

        public static bool HasCycle<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            where TEdgeData : class
        {
            var analyzer = new DisposeOperations.DefaultGraphAnalyzer<TKey, TNodeData, TEdgeData>();
            return analyzer.HasCycle(graph);
        }

        public static IReadOnlyList<TKey> FindCriticalPath<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph,
            TKey startKey,
            TKey endKey)
            where TKey : notnull
            where TNodeData : class
            where TEdgeData : class
        {
            var analyzer = new DisposeOperations.DefaultGraphAnalyzer<TKey, TNodeData, TEdgeData>();
            return analyzer.FindCriticalPath(graph, startKey, endKey);
        }

        public static IReadOnlyList<IReadOnlyList<TKey>> FindConnectedComponents<TKey, TNodeData, TEdgeData>(
            IReadOnlyGraph<TKey, TNodeData, TEdgeData> graph)
            where TKey : notnull
            where TNodeData : class
            where TEdgeData : class
        {
            var analyzer = new DisposeOperations.DefaultGraphAnalyzer<TKey, TNodeData, TEdgeData>();
            return analyzer.FindConnectedComponents(graph);
        }

        #endregion
    }
}
