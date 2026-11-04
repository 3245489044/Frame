
#region USINGS
using System;
using GraphFramework.Models;
using System.Collections.Generic;
using System.Linq;
using GraphFoundation.Chain;
using Modules.GraphProcessing.Normalization;
using Modules.GraphProcessing.Utilities;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.DefaultImplementations;
using Share.LogicComposition.Models;
using WorkflowExecution.Abstractions;
using WorkFlowMethod.Diagnostics;
using WorkFlowMethod.Infrastructure.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Deduplication
{
    public class ConfigDeduplicationProcessor
    {
        private readonly IChainNodeHandler<NodeConfig, Chain<string, NodeConfig>> _nodeHandler;
        private readonly INormalizationConfig _config;
        private readonly INameGenerator _nameGenerator;
        private readonly INodeRepository<NodeConfig> _nodeRepository;
        private readonly IdentifierExtractor _identifierExtractor;
        private readonly ConfigRelationHandler _relationHandler;

        private readonly IChainComposer<NodeConfig> _chainComposer;

        public ConfigDeduplicationProcessor(
            IChainNodeHandler<NodeConfig, Chain<string, NodeConfig>> nodeHandler,
            INormalizationConfig config,
            INameGenerator nameGenerator,
            INodeRepository<NodeConfig> nodeRepository,
            ConfigRelationHandler? relationHandler = null)
        {
            _nodeHandler = nodeHandler ?? throw new ArgumentNullException(nameof(nodeHandler));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _nameGenerator = nameGenerator ?? throw new ArgumentNullException(nameof(nameGenerator));
            _nodeRepository = nodeRepository ?? throw new ArgumentNullException(nameof(nodeRepository));
            _identifierExtractor = new IdentifierExtractor();
            _relationHandler = relationHandler ?? new ConfigRelationHandler(nodeHandler);

            _chainComposer = ChainComposerFactory.Create();
        }

        public List<Chain<string, NodeConfig>> Deduplicate(List<Chain<string, NodeConfig>> chains)
        {
            if (chains == null || chains.Count == 0)
                return new List<Chain<string, NodeConfig>>();

            var identifierMap = BuildIdentifierMap(chains);

            var (renameMap, nodeRenames) = ResolveDuplicates(identifierMap);

            ApplyRenames(chains, renameMap, nodeRenames);

            var allNodeSequences = chains
                .Where(c => c != null)
                .Select(c => _nodeHandler.GetNodes(c))
                .Where(nodes => nodes != null && nodes.Any())
                .ToList();

            var resultChains = chains;

            if (allNodeSequences.Count > 0)
            {
                var composeResult = _chainComposer.Compose(
                    allNodeSequences,
                    new CompositionOptions
                    {
                        DuplicateHandling = DuplicateHandling.Skip,
                        ValidateBeforeMerge = _config.ValidateIntegrity,
                        TrackConflicts = _config.VerboseLogging
                    });

                if (composeResult.IsSuccess && composeResult.Result != null)
                {
                    // ERR-05：合并结果必须作为返回值落地，否则「去重合并」只是空转。
                    var mergedNodes = composeResult.Result.ToList();
                    if (mergedNodes.Count > 0)
                    {
                        resultChains = new List<Chain<string, NodeConfig>>
                        {
                            new Chain<string, NodeConfig>
                            {
                                AllNodes = mergedNodes,
                                StartNode = mergedNodes.FirstOrDefault(),
                                EndNode = mergedNodes.LastOrDefault()
                            }
                        };
                    }
                }
                else if (_config.VerboseLogging)
                {
                    WorkFlowMethodLog.Log("ConfigDeduplicationProcessor_ComposeWarning",
                        new { error = composeResult.ErrorMessage });
                }
            }

            foreach (var chain in resultChains)
            {
                foreach (var node in _nodeHandler.GetNodes(chain))
                {
                    var key = _nodeHandler.GetKey(node);
                    if (!string.IsNullOrEmpty(key))
                        _nodeRepository.AddOrUpdateNode(key, node);
                }
            }

            return resultChains;
        }

        private Dictionary<string, List<(Chain<string, NodeConfig> chain, NodeConfig node)>> BuildIdentifierMap(
            List<Chain<string, NodeConfig>> chains)
        {
            var map = new Dictionary<string, List<(Chain<string, NodeConfig>, NodeConfig)>>();

            foreach (var chain in chains)
            {
                foreach (var node in _nodeHandler.GetNodes(chain))
                {
                    var key = _nodeHandler.GetKey(node);
                    if (string.IsNullOrEmpty(key))
                        continue;

                    var normalizedKey = _identifierExtractor.Extract(key);

                    if (!map.ContainsKey(normalizedKey))
                        map[normalizedKey] = new List<(Chain<string, NodeConfig>, NodeConfig)>();

                    map[normalizedKey].Add((chain, node));
                }
            }

            return map;
        }

        private (Dictionary<string, string> RenameMap, List<(Chain<string, NodeConfig> chain, NodeConfig node, string newKey)> NodeRenames) ResolveDuplicates(
            Dictionary<string, List<(Chain<string, NodeConfig> chain, NodeConfig node)>> identifierMap)
        {
            var renameMap = new Dictionary<string, string>();
            var nodeRenames = new List<(Chain<string, NodeConfig>, NodeConfig, string)>();

            foreach (var kvp in identifierMap)
            {
                var normalizedKey = kvp.Key;
                var entries = kvp.Value;

                if (entries.Count <= 1)
                    continue;

                for (int i = 1; i < entries.Count; i++)
                {
                    var originalKey = _nodeHandler.GetKey(entries[i].node);
                    if (string.IsNullOrEmpty(originalKey))
                        continue;

                    var newKey = _nameGenerator.GenerateName(normalizedKey);
                    renameMap[originalKey] = newKey;
                    nodeRenames.Add((entries[i].chain, entries[i].node, newKey));
                }
            }

            return (renameMap, nodeRenames);
        }

        private void ApplyRenames(
            List<Chain<string, NodeConfig>> chains,
            Dictionary<string, string> renameMap,
            List<(Chain<string, NodeConfig> chain, NodeConfig node, string newKey)> nodeRenames)
        {
            if (renameMap.Count == 0)
                return;

            foreach (var (chain, node, newKey) in nodeRenames)
            {
                var originalKey = _nodeHandler.GetKey(node);
                if (string.IsNullOrEmpty(originalKey))
                    continue;

                _nodeHandler.RemoveNode(chain, originalKey);

                // ERR-04：SetNode 依据节点自身键写入，必须先改键再设置，否则注册表仍是旧键。
                SetNodeKey(node, newKey);
                _nodeHandler.SetNode(chain, node);
            }

            foreach (var chain in chains)
            {
                foreach (var node in _nodeHandler.GetNodes(chain))
                {
                    _relationHandler.UpdateReferences(node, renameMap);
                }
            }
        }

        private static void SetNodeKey(NodeConfig node, string newKey)
        {
            node.basicConfig ??= new NodeConfig.BasicConfig();
            node.basicConfig.Callkey = newKey;
        }
    }
}
