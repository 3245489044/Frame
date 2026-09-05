
#nullable enable

using GraphFoundation.Abstractions;
using GraphFoundation.Core.Commands;
using Modules.GraphProcessing.Utilities;

namespace Modules.GraphProcessing.Orchestration
{

    public interface IKeyGenerator<TKey> where TKey : notnull
    {
        TKey GenerateNext();
    }

    public interface INodeGenerator<TNodeData> where TNodeData : class
    {
        TNodeData Clone(TNodeData template);
    }

    public interface IParameterProcessor<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        void ProcessParameters(ITransaction transaction, IList<TNodeData> nodes, ICoreGraph<TKey, TNodeData, TEdgeData> graph);
    }

    public interface IEdgeManager<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        void AddEdges(ITransaction transaction, IList<TKey> nodeKeys, string chainId);

        void RemoveEdges(ITransaction transaction, IEnumerable<(TKey, TKey)> edgeKeys);

        void Rebuild(ITransaction transaction, IList<TKey> nodeKeys, IEnumerable<(TKey, TKey)> oldEdgeKeys, string chainId, ICoreGraph<TKey, TNodeData, TEdgeData> graph);
    }

    public interface IChainBuilder<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        ChainResult<TKey> CreateFromTemplate(TNodeData template, int count, IKeyGenerator<TKey> keyGenerator, ICoreGraph<TKey, TNodeData, TEdgeData> graph);
    }

    public interface IChainMerger<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        ChainResult<TKey> Merge(IEnumerable<ChainResult<TKey>> chains, ICoreGraph<TKey, TNodeData, TEdgeData> graph);
    }


    public class ChainResult<TKey> where TKey : notnull
    {
        public IReadOnlyList<TKey> NodeKeys { get; }

        public IReadOnlyList<(TKey Source, TKey Target)> EdgeKeys { get; }

        public string ChainId { get; }

        public ChainResult(IEnumerable<TKey> nodeKeys, IEnumerable<(TKey Source, TKey Target)> edgeKeys, string? chainId = null)
        {
            NodeKeys = nodeKeys?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(nodeKeys));
            EdgeKeys = edgeKeys?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(edgeKeys));
            ChainId = chainId ?? Guid.NewGuid().ToString("N");
        }
    }

    public class OrchestrationResult<TKey> where TKey : notnull
    {
        public int ChainsCreated { get; set; }

        public int ChainsMerged { get; set; }

        public int NodesCreated { get; set; }

        public int EdgesCreated { get; set; }

        public List<string> Log { get; } = new();

        public List<ChainResult<TKey>> CreatedChains { get; } = new();

        public IReadOnlyList<ChainResult<TKey>> Chains => CreatedChains;

        public ChainResult<TKey>? MergedChain { get; set; }
    }


    public class DefaultKeyGenerator<TKey> : IKeyGenerator<TKey> where TKey : notnull
    {
        private int _counter;
        private readonly Func<int, TKey> _converter;

        public DefaultKeyGenerator(Func<int, TKey>? converter = null)
        {
            _converter = converter ?? (i => (TKey)(object)i.ToString());
        }

        public TKey GenerateNext()
        {
            var key = _converter(Interlocked.Increment(ref _counter));
            return key;
        }

        public void Reset() => _counter = 0;
    }

    public class DefaultNodeGenerator<TNodeData> : INodeGenerator<TNodeData>
        where TNodeData : class
    {
        private readonly Func<TNodeData, TNodeData> _cloner;

        public DefaultNodeGenerator(Func<TNodeData, TNodeData>? cloner = null)
        {
            _cloner = cloner ?? DeepCopyUtility.DeepCopy;
        }

        public TNodeData Clone(TNodeData template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            return _cloner(template);
        }
    }

    public class PositionAssigner<TKey, TNodeData, TEdgeData> : IParameterProcessor<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly Action<TNodeData, int> _setPosition;
        private readonly Func<TNodeData, TKey> _keyExtractor;

        public PositionAssigner(Action<TNodeData, int> setPosition, Func<TNodeData, TKey> keyExtractor)
        {
            _setPosition = setPosition ?? throw new ArgumentNullException(nameof(setPosition));
            _keyExtractor = keyExtractor ?? throw new ArgumentNullException(nameof(keyExtractor));
        }

        public void ProcessParameters(ITransaction transaction, IList<TNodeData> nodes, ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                    throw new ArgumentNullException($"Node at index {i} is null.");

                _setPosition(node, i);

                var key = _keyExtractor(node);
                if (key != null)
                {
                    transaction.Enqueue(new UpdateNodeCommand<TKey, TNodeData, TEdgeData>(key, node));
                }
            }
        }
    }

    public class DefaultEdgeManager<TKey, TNodeData, TEdgeData> : IEdgeManager<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        public void AddEdges(ITransaction transaction, IList<TKey> nodeKeys, string chainId)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (nodeKeys == null) throw new ArgumentNullException(nameof(nodeKeys));
            if (chainId == null) throw new ArgumentNullException(nameof(chainId));

            if (nodeKeys.Count < 2) return;

            for (int i = 0; i < nodeKeys.Count - 1; i++)
            {
                var source = nodeKeys[i];
                var target = nodeKeys[i + 1];
                if (source == null || target == null)
                    throw new ArgumentNullException(source == null ? nameof(source) : nameof(target));

                var edgeId = $"{chainId}_e_{i}";
                transaction.Enqueue(new AddEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId, source, target, true, default, 1.0));
            }
        }

        public void RemoveEdges(ITransaction transaction, IEnumerable<(TKey, TKey)> edgeKeys)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (edgeKeys == null) throw new ArgumentNullException(nameof(edgeKeys));

            int index = 0;
            foreach (var (source, target) in edgeKeys)
            {
                var edgeId = $"edge_{index}_{source}_{target}";
                transaction.Enqueue(new RemoveEdgeCommand<TKey, TNodeData, TEdgeData>(edgeId));
                index++;
            }
        }



        public void Rebuild(ITransaction transaction, IList<TKey> nodeKeys, IEnumerable<(TKey, TKey)> oldEdgeKeys, string chainId, ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (nodeKeys == null) throw new ArgumentNullException(nameof(nodeKeys));
            if (oldEdgeKeys == null) throw new ArgumentNullException(nameof(oldEdgeKeys));
            if (chainId == null) throw new ArgumentNullException(nameof(chainId));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            RemoveEdges(transaction, oldEdgeKeys);
            AddEdges(transaction, nodeKeys, chainId);
        }
    }

    public class ChainBuilder<TKey, TNodeData, TEdgeData> : IChainBuilder<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly INodeGenerator<TNodeData> _generator;
        private readonly IParameterProcessor<TKey, TNodeData, TEdgeData> _processor;
        private readonly IEdgeManager<TKey, TNodeData, TEdgeData> _edgeManager;
        private readonly Func<TNodeData, TKey> _keyExtractor;

        public ChainBuilder(
            INodeGenerator<TNodeData> generator,
            IParameterProcessor<TKey, TNodeData, TEdgeData> processor,
            IEdgeManager<TKey, TNodeData, TEdgeData> edgeManager,
            Func<TNodeData, TKey> keyExtractor)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _edgeManager = edgeManager ?? throw new ArgumentNullException(nameof(edgeManager));
            _keyExtractor = keyExtractor ?? throw new ArgumentNullException(nameof(keyExtractor));
        }

        public ChainResult<TKey> CreateFromTemplate(
            TNodeData template,
            int count,
            IKeyGenerator<TKey> keyGenerator,
            ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (keyGenerator == null) throw new ArgumentNullException(nameof(keyGenerator));
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (count <= 0) throw new ArgumentException("Count must be greater than zero", nameof(count));

            using var tx = graph.BeginTransaction();

            var (nodes, nodeKeys) = CreateNodes(template, count, keyGenerator, graph, tx);
            _processor.ProcessParameters(tx, nodes, graph);

            var chainId = Guid.NewGuid().ToString("N");
            var edgeKeys = CreateEdges(tx, nodeKeys, chainId);

            tx.Commit();

            return new ChainResult<TKey>(nodeKeys, edgeKeys, chainId);
        }

        private (List<TNodeData> Nodes, List<TKey> NodeKeys) CreateNodes(
            TNodeData template,
            int count,
            IKeyGenerator<TKey> keyGenerator,
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            ITransaction tx)
        {
            var nodes = new List<TNodeData>(count);
            var nodeKeys = new List<TKey>(count);
            var usedKeys = new HashSet<TKey>();

            for (int i = 0; i < count; i++)
            {
                var clone = _generator.Clone(template);
                var key = GenerateUniqueKey(keyGenerator, graph, usedKeys);
                usedKeys.Add(key);
                nodeKeys.Add(key);
                nodes.Add(clone);
                tx.Enqueue(new AddNodeCommand<TKey, TNodeData, TEdgeData>(key, clone));
            }

            return (nodes, nodeKeys);
        }

        private static TKey GenerateUniqueKey(
            IKeyGenerator<TKey> keyGenerator,
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            HashSet<TKey> usedKeys)
        {
            TKey key;
            do
            {
                key = keyGenerator.GenerateNext();
            } while (graph.ContainsNode(key) || usedKeys.Contains(key));
            return key;
        }

        private List<(TKey Source, TKey Target)> CreateEdges(ITransaction tx, List<TKey> nodeKeys, string chainId)
        {
            _edgeManager.AddEdges(tx, nodeKeys, chainId);

            var edgeKeys = new List<(TKey, TKey)>(Math.Max(0, nodeKeys.Count - 1));
            for (int i = 0; i < edgeKeys.Capacity; i++)
            {
                edgeKeys.Add((nodeKeys[i], nodeKeys[i + 1]));
            }
            return edgeKeys;
        }
    }

    public class ChainMerger<TKey, TNodeData, TEdgeData> : IChainMerger<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly IEdgeManager<TKey, TNodeData, TEdgeData> _edgeManager;

        public ChainMerger(IEdgeManager<TKey, TNodeData, TEdgeData> edgeManager)
        {
            _edgeManager = edgeManager ?? throw new ArgumentNullException(nameof(edgeManager));
        }

        public ChainResult<TKey> Merge(
            IEnumerable<ChainResult<TKey>> chains,
            ICoreGraph<TKey, TNodeData, TEdgeData> graph)
        {
            if (chains == null) throw new ArgumentNullException(nameof(chains));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var chainList = chains.ToList();
            if (chainList.Count == 0)
                throw new ArgumentException("At least one chain must be provided", nameof(chains));

            var allKeys = chainList.SelectMany(c => c.NodeKeys).ToList();
            if (allKeys.Any(key => key == null))
                throw new ArgumentException("Chain contains null keys", nameof(chains));

            var chainId = Guid.NewGuid().ToString("N");

            using var tx = graph.BeginTransaction();
            _edgeManager.Rebuild(tx, allKeys, chainList.SelectMany(c => c.EdgeKeys), chainId, graph);
            tx.Commit();

            var edgeKeys = new List<(TKey, TKey)>(Math.Max(0, allKeys.Count - 1));
            for (int i = 0; i < edgeKeys.Capacity; i++)
            {
                edgeKeys.Add((allKeys[i], allKeys[i + 1]));
            }

            return new ChainResult<TKey>(allKeys, edgeKeys, chainId);
        }
    }


    public class OrchestrationService<TKey, TNodeData, TEdgeData>
        where TKey : notnull
        where TNodeData : class
    {
        private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
        private readonly IChainBuilder<TKey, TNodeData, TEdgeData> _chainBuilder;
        private readonly IChainMerger<TKey, TNodeData, TEdgeData> _chainMerger;
        private readonly IEdgeManager<TKey, TNodeData, TEdgeData> _edgeManager;

        public OrchestrationService(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            Func<TNodeData, TKey>? keyExtractor = null,
            Action<TNodeData, int>? setPosition = null,
            Func<TNodeData, TNodeData>? cloner = null,
            Func<int, TKey>? keyConverter = null)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));

            var defaultKeyExtractor = keyExtractor ?? (data => default!);
            var defaultSetPosition = setPosition ?? ((data, pos) => { });

            var generator = new DefaultNodeGenerator<TNodeData>(cloner);
            var processor = new PositionAssigner<TKey, TNodeData, TEdgeData>(defaultSetPosition, defaultKeyExtractor);
            _edgeManager = new DefaultEdgeManager<TKey, TNodeData, TEdgeData>();
            _chainBuilder = new ChainBuilder<TKey, TNodeData, TEdgeData>(generator, processor, _edgeManager, defaultKeyExtractor);
            _chainMerger = new ChainMerger<TKey, TNodeData, TEdgeData>(_edgeManager);
        }

        public OrchestrationService(
            ICoreGraph<TKey, TNodeData, TEdgeData> graph,
            IChainBuilder<TKey, TNodeData, TEdgeData> chainBuilder,
            IChainMerger<TKey, TNodeData, TEdgeData> chainMerger,
            IEdgeManager<TKey, TNodeData, TEdgeData> edgeManager)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _chainBuilder = chainBuilder ?? throw new ArgumentNullException(nameof(chainBuilder));
            _chainMerger = chainMerger ?? throw new ArgumentNullException(nameof(chainMerger));
            _edgeManager = edgeManager ?? throw new ArgumentNullException(nameof(edgeManager));
        }


        public ChainResult<TKey> CreateChainFromTemplate(
            TNodeData template,
            int count,
            IKeyGenerator<TKey>? keyGenerator = null)
        {
            keyGenerator ??= new DefaultKeyGenerator<TKey>(i => (TKey)(object)i.ToString());
            return _chainBuilder.CreateFromTemplate(template, count, keyGenerator, _graph);
        }

        public OrchestrationResult<TKey> CreateMultipleChains(
            TNodeData template,
            int chainCount,
            int nodesPerChain,
            IKeyGenerator<TKey>? keyGenerator = null)
        {
            keyGenerator ??= new DefaultKeyGenerator<TKey>(i => (TKey)(object)i.ToString());
            var result = new OrchestrationResult<TKey>();

            for (int i = 0; i < chainCount; i++)
            {
                var chain = CreateChainFromTemplate(template, nodesPerChain, keyGenerator);
                result.CreatedChains.Add(chain);
                result.ChainsCreated++;
                result.NodesCreated += chain.NodeKeys.Count;
                result.EdgesCreated += chain.EdgeKeys.Count;
                result.Log.Add($"Created chain {i + 1}: {chain.NodeKeys.Count} nodes, {chain.EdgeKeys.Count} edges");
            }

            return result;
        }


        public ChainResult<TKey> ConnectChains(IEnumerable<ChainResult<TKey>> chains)
        {
            return _chainMerger.Merge(chains, _graph);
        }

        public OrchestrationResult<TKey> MergeAllChains(IEnumerable<ChainResult<TKey>> createdChains)
        {
            var result = new OrchestrationResult<TKey>();
            var chainList = createdChains.ToList();

            if (chainList.Count == 0)
            {
                result.Log.Add("No chains to merge.");
                return result;
            }

            var merged = _chainMerger.Merge(chainList, _graph);
            result.MergedChain = merged;
            result.ChainsMerged = chainList.Count;
            result.Log.Add($"Merged {chainList.Count} chains into one: {merged.NodeKeys.Count} nodes, {merged.EdgeKeys.Count} edges");

            return result;
        }


        public void RebuildSequentialRelations(IList<TKey> nodeKeys, string chainId)
        {
            if (nodeKeys == null) throw new ArgumentNullException(nameof(nodeKeys));
            if (chainId == null) throw new ArgumentNullException(nameof(chainId));

            using var tx = _graph.BeginTransaction();
            _edgeManager.Rebuild(tx, nodeKeys, Enumerable.Empty<(TKey, TKey)>(), chainId, _graph);
            tx.Commit();
        }


        public static IKeyGenerator<TKey> CreateDefaultKeyGenerator(Func<int, TKey>? converter = null)
            => new DefaultKeyGenerator<TKey>(converter);

        public static INodeGenerator<TNodeData> CreateDefaultNodeGenerator(Func<TNodeData, TNodeData>? cloner = null)
            => new DefaultNodeGenerator<TNodeData>(cloner);

        public static IEdgeManager<TKey, TNodeData, TEdgeData> CreateDefaultEdgeManager()
            => new DefaultEdgeManager<TKey, TNodeData, TEdgeData>();

        public static IChainBuilder<TKey, TNodeData, TEdgeData> CreateChainBuilder(
            INodeGenerator<TNodeData> generator,
            IParameterProcessor<TKey, TNodeData, TEdgeData> processor,
            IEdgeManager<TKey, TNodeData, TEdgeData> edgeManager,
            Func<TNodeData, TKey> keyExtractor)
            => new ChainBuilder<TKey, TNodeData, TEdgeData>(generator, processor, edgeManager, keyExtractor);

        public static IChainMerger<TKey, TNodeData, TEdgeData> CreateChainMerger(
            IEdgeManager<TKey, TNodeData, TEdgeData> edgeManager)
            => new ChainMerger<TKey, TNodeData, TEdgeData>(edgeManager);
    }
}
