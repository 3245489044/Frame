using DictionaryAlgorithm;
using Graphkey.Core;
using Modules.Common.Cloning;
using System.Text.Encodings.Web;
using System.Text.Json;

#region GraphKey

namespace Graphkey.Core
{
    internal static class ExceptionMessages
    {
        public const string CannotUseBothIndexAndKey = "Cannot use both index and key.";
        public const string IndexAlreadySet = "Index already set.";
        public const string KeyAlreadySet = "Key already set.";
        public const string InputPlaceholderMustHaveKey = "Input placeholder must have a key.";
        public const string CannotReferencePrevNoOutputs = "Cannot reference previous node because it has no outputs.";
        public const string NodeReferenceMustHaveNodeId = "NodeReference placeholder must have NodeId.";
        public const string NodeIndexMustHaveNodeIndex = "NodeIndex placeholder must have NodeIndex.";
        public const string InputKeyCannotBeNullOrEmpty = "Input key cannot be null or empty.";
        public const string NodeMustDefineResultKeys = "Node must define result keys.";
        public const string DuplicateNodeId = "Duplicate node ID '{0}'.";
        public const string NodeIdNotFound = "Node ID '{0}' not found.";
        public const string ParameterKeyCannotBeNull = "Parameter key cannot be null in node '{0}'.";
        public const string ParameterKeyMustBeString = "Parameter key must be a string, got {0} in node '{1}'.";
        public const string ParameterKeyCannotBeEmpty = "Parameter key cannot be empty in node '{0}'.";
        public const string UndefinedParameterKey = "Parameter key '{0}' in node '{1}' is not defined.";
        public const string OutputKeyCannotBeNullOrEmpty = "Output key cannot be null or empty in node '{0}'.";
        public const string OutputKeyAlreadyDefined = "Output key '{0}' in node '{1}' is already defined.";
        public const string OutputKeysContainDuplicates = "Output keys cannot contain duplicates. Node: {0}";
        public const string IndexOutOfRange = "Index {0} out of range for {1}. Available: {2}";
        public const string KeyNotFound = "Key '{0}' not found in {1} outputs. Available: {2}";
        public const string AmbiguousReference = "Ambiguous reference to {0}: multiple outputs ({1}). Use .Index or .Key.";
        public const string NodeHasNoOutputs = "Node '{0}' has no outputs.";
        public const string NodeAtIndexHasNoOutputs = "Node at index {0} has no outputs.";
        public const string UnexpectedParameterType = "Unexpected parameter type: {0}";
        public const string ParameterMustBeStringOrPlaceholder = "Parameter must be string or Placeholder, got {0}";
        public const string UnsupportedPlaceholderType = "Unsupported placeholder type: {0}";
        public const string CollectionEmptyCannotIndex = "The outputs collection is empty, cannot resolve index {0}.";

    }

    public interface IPlaceholder { }

    public enum PlaceholderKind { Input, Prev, NodeReference, NodeIndex }

    public sealed record Placeholder : IPlaceholder
    {
        public PlaceholderKind Kind { get; init; }
        public string Key { get; init; }
        public int? Index { get; init; }
        public string NodeId { get; init; }
        public int? NodeIndex { get; init; }

        private Placeholder(PlaceholderKind kind, string key = null, int? index = null, string nodeId = null, int? nodeIndex = null)
            => (Kind, Key, Index, NodeId, NodeIndex) = (kind, key, index, nodeId, nodeIndex);

        public static Placeholder Input(string key) => new(PlaceholderKind.Input, key: key);
        public static Placeholder Prev() => new(PlaceholderKind.Prev);
        public static Placeholder PrevByIndex(int index) => new(PlaceholderKind.Prev, index: index);
        public static Placeholder PrevByKey(string key) => new(PlaceholderKind.Prev, key: key);
        public static Placeholder From(string nodeId) => new(PlaceholderKind.NodeReference, nodeId: nodeId);
        public static Placeholder From(string nodeId, int index) => new(PlaceholderKind.NodeReference, nodeId: nodeId, index: index);
        public static Placeholder From(string nodeId, string key) => new(PlaceholderKind.NodeReference, nodeId: nodeId, key: key);
        public static Placeholder Node(int nodeIndex) => new(PlaceholderKind.NodeIndex, nodeIndex: nodeIndex);

        private void EnsureIndexerSupported()
        {
            if (Kind != PlaceholderKind.Prev && Kind != PlaceholderKind.NodeReference && Kind != PlaceholderKind.NodeIndex && Kind != PlaceholderKind.Input)
                throw new InvalidOperationException($"Indexer not supported for {Kind}.");
        }

        public Placeholder this[int index]
        {
            get
            {
                EnsureIndexerSupported();

                return this with { Index = index, Key = null };
            }
        }

        public Placeholder this[string key]
        {
            get
            {
                EnsureIndexerSupported();

                return this with { Key = key, Index = null };
            }
        }
    }

    public interface IResolutionContext
    {
        IReadOnlyList<string> PreviousOutputs { get; }
        IReadOnlyList<string> GetNodeOutputs(string nodeId);
        IReadOnlyList<string> GetNodeOutputs(int index);
        IReadOnlyCollection<string> InputKeys { get; }
    }

    public interface INodeHandler
    {
        string GetNodeId(object node);
        IEnumerable<object> GetParameterKeys(object node);
        IEnumerable<string> GetResultKeys(object node);
        void SetParameterKeys(object node, IEnumerable<object> keys);
        string GetDisplayName(object node);
        object CloneNode(object node);
    }

    public interface IChainNodeHandler<TNode, TChain> where TNode : class where TChain : class
    {
        string GetKey(TNode node);
        void SetKey(TNode node, string key);
        TChain CloneChain(TChain chain);
        IList<TNode> GetNodes(TChain chain);
        void SetNodes(TChain chain, IList<TNode> nodes);
        TNode GetStartNode(TChain chain);
        void SetStartNode(TChain chain, TNode node);
        TNode GetEndNode(TChain chain);
        void SetEndNode(TChain chain, TNode node);
        void UpdateReferences(TNode node, Func<string, TNode, string> resolve);
        string GetSymbol(TNode node);
        void SetSymbol(TNode node, string symbol);
        IEnumerable<string> GetAdditionalSymbols(TNode node);
        void SetAdditionalSymbols(TNode node, IEnumerable<string> symbols);
        TNode CloneNode(TNode node);
    }

    internal static class KeyValidation
    {
        public static void EnsureNoNullOrEmpty(IEnumerable<string> keys, string keyType, string nodeDisplayName)
        {
            int i = 0;
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k))
                    throw new ArgumentException($"{keyType} key at position {i} cannot be null or empty. Node: {nodeDisplayName}");
                i++;
            }
        }
    }

    public static class PlaceholderResolver
    {
        public static string Resolve(IPlaceholder placeholder, IResolutionContext context) => placeholder switch
        {
            Placeholder { Kind: PlaceholderKind.Input, Key: var key } => key ?? throw new InvalidOperationException(ExceptionMessages.InputPlaceholderMustHaveKey),
            Placeholder { Kind: PlaceholderKind.Prev } p => ResolvePrev(p, context.PreviousOutputs),
            Placeholder { Kind: PlaceholderKind.NodeReference } p => ResolveNodeRef(p, context.GetNodeOutputs),
            Placeholder { Kind: PlaceholderKind.NodeIndex } p => ResolveNodeIndex(p, context.GetNodeOutputs),
            _ => throw new NotSupportedException(string.Format(ExceptionMessages.UnsupportedPlaceholderType, placeholder?.GetType()))
        };

        private static string ResolvePrev(Placeholder p, IReadOnlyList<string> prev) =>
            prev == null || prev.Count == 0 ? throw new InvalidOperationException(ExceptionMessages.CannotReferencePrevNoOutputs)
            : ResolveIndexOrKey(p, prev, "previous node");

        private static string ResolveNodeRef(Placeholder p, Func<string, IReadOnlyList<string>> getOutputs)
        {
            if (string.IsNullOrEmpty(p.NodeId)) throw new ArgumentException(ExceptionMessages.NodeReferenceMustHaveNodeId);
            var outputs = getOutputs(p.NodeId) ?? throw new KeyNotFoundException(string.Format(ExceptionMessages.NodeIdNotFound, p.NodeId));
            if (outputs.Count == 0) throw new InvalidOperationException(string.Format(ExceptionMessages.NodeHasNoOutputs, p.NodeId));
            return ResolveIndexOrKey(p, outputs, $"node '{p.NodeId}'");
        }

        private static string ResolveNodeIndex(Placeholder p, Func<int, IReadOnlyList<string>> getOutputs)
        {
            if (!p.NodeIndex.HasValue) throw new ArgumentException(ExceptionMessages.NodeIndexMustHaveNodeIndex);
            var outputs = getOutputs(p.NodeIndex.Value) ?? throw new ArgumentOutOfRangeException(nameof(p.NodeIndex), string.Format(ExceptionMessages.NodeAtIndexHasNoOutputs, p.NodeIndex.Value));
            if (outputs.Count == 0) throw new InvalidOperationException(string.Format(ExceptionMessages.NodeAtIndexHasNoOutputs, p.NodeIndex.Value));
            return ResolveIndexOrKey(p, outputs, $"node at index {p.NodeIndex}");
        }

        private static string ResolveIndexOrKey(Placeholder p, IReadOnlyList<string> outputs, string targetDesc)
        {
            if (!p.Index.HasValue && string.IsNullOrEmpty(p.Key))
            {
                if (outputs.Count > 1)
                    throw new InvalidOperationException(string.Format(ExceptionMessages.AmbiguousReference, targetDesc, string.Join(", ", outputs)));
                return outputs[0];
            }

            if (p.Index.HasValue && !string.IsNullOrEmpty(p.Key))
                throw new InvalidOperationException(ExceptionMessages.CannotUseBothIndexAndKey);

            if (p.Index.HasValue)
            {
                int idx = p.Index.Value;
                if (idx < 0 || idx >= outputs.Count)
                    throw new ArgumentOutOfRangeException(nameof(p.Index), string.Format(ExceptionMessages.IndexOutOfRange, idx, targetDesc, string.Join(", ", outputs)));
                return outputs[idx];
            }

            return outputs.Contains(p.Key) ? p.Key : throw new KeyNotFoundException(string.Format(ExceptionMessages.KeyNotFound, p.Key, targetDesc, string.Join(", ", outputs)));
        }

        public static void ResolvePlaceholders(List<object> nodes, INodeHandler handler)
        {
            var nodeIdToOutputs = new Dictionary<string, IReadOnlyList<string>>();
            var allNodeOutputs = new List<IReadOnlyList<string>>();

            foreach (var node in nodes)
            {
                string nodeId = handler.GetNodeId(node);
                var outputs = handler.GetResultKeys(node)?.ToList();
                IReadOnlyList<string> outputsReadOnly = outputs != null ? outputs.AsReadOnly() : Array.Empty<string>();
                allNodeOutputs.Add(outputsReadOnly);
                if (!string.IsNullOrEmpty(nodeId) && !nodeIdToOutputs.TryAdd(nodeId, outputsReadOnly))
                    throw new InvalidOperationException(string.Format(ExceptionMessages.DuplicateNodeId, nodeId));
            }

            IReadOnlyList<string> previousOutputs = Array.Empty<string>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var paramList = handler.GetParameterKeys(node)?.ToList();
                var resolved = new List<object>();
                if (paramList != null)
                {
                    foreach (var p in paramList)
                    {
                        resolved.Add(p switch
                        {
                            string s => s,
                            IPlaceholder ph => Resolve(ph, new SimpleContext(previousOutputs, nodeIdToOutputs, allNodeOutputs)),
                            _ => throw new InvalidOperationException(string.Format(ExceptionMessages.UnexpectedParameterType, p?.GetType()))
                        });
                    }
                }
                handler.SetParameterKeys(node, resolved);
                previousOutputs = allNodeOutputs[i];
            }
        }

        private record SimpleContext(IReadOnlyList<string> PreviousOutputs, Dictionary<string, IReadOnlyList<string>> NodeIdToOutputs, IReadOnlyList<IReadOnlyList<string>> AllNodeOutputs) : IResolutionContext
        {
            public IReadOnlyCollection<string> InputKeys => Array.Empty<string>();
            public IReadOnlyList<string> GetNodeOutputs(string nodeId) => NodeIdToOutputs.TryGetValue(nodeId, out var o) ? o : throw new KeyNotFoundException(string.Format(ExceptionMessages.NodeIdNotFound, nodeId));
            public IReadOnlyList<string> GetNodeOutputs(int index)
            {
                if (AllNodeOutputs.Count == 0)
                    throw new ArgumentOutOfRangeException(nameof(index), string.Format(ExceptionMessages.CollectionEmptyCannotIndex, index));

                int idx = index < 0 ? AllNodeOutputs.Count + index : index;
                if (idx < 0 || idx >= AllNodeOutputs.Count)
                    throw new ArgumentOutOfRangeException(nameof(index), $"Node index {index} out of range. Total nodes: {AllNodeOutputs.Count}");
                return AllNodeOutputs[idx];
            }
        }
    }

    internal static class WorkflowValidator
    {
        public static void Validate(IReadOnlyList<object> nodes, IReadOnlyCollection<string> inputKeys, INodeHandler handler)
        {
            var knownKeys = new HashSet<string>(inputKeys);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                string display = handler.GetDisplayName(node) ?? $"Node_{i}";

                var rawParamKeys = handler.GetParameterKeys(node);
                var paramKeys = rawParamKeys?.Select(p =>
                {
                    if (p == null) throw new InvalidOperationException(string.Format(ExceptionMessages.ParameterKeyCannotBeNull, display));
                    if (p is IPlaceholder) return null;
                    if (p is not string s) throw new InvalidOperationException(string.Format(ExceptionMessages.ParameterKeyMustBeString, p.GetType(), display));
                    if (string.IsNullOrEmpty(s)) throw new InvalidOperationException(string.Format(ExceptionMessages.ParameterKeyCannotBeEmpty, display));
                    return s;
                }).Where(k => k != null) ?? Enumerable.Empty<string>();

                string unknown = paramKeys.FirstOrDefault(k => !knownKeys.Contains(k));
                if (unknown != null) throw new InvalidOperationException(string.Format(ExceptionMessages.UndefinedParameterKey, unknown, display));

                var resultKeys = handler.GetResultKeys(node) ?? Enumerable.Empty<string>();
                foreach (var key in resultKeys)
                {
                    if (string.IsNullOrEmpty(key)) throw new InvalidOperationException(string.Format(ExceptionMessages.OutputKeyCannotBeNullOrEmpty, display));
                    if (!knownKeys.Add(key)) throw new InvalidOperationException(string.Format(ExceptionMessages.OutputKeyAlreadyDefined, key, display));
                }
            }
        }
    }

    public class DynamicWorkflowBuilder
    {
        private readonly List<object> _nodes = new();
        private readonly HashSet<string> _nodeIds = new();
        private readonly HashSet<string> _inputKeys = new();
        private readonly Dictionary<string, List<string>> _nodeIdToResultKeys = new();
        private bool _validateOnBuild = true;
        private readonly INodeHandler _handler;

        public DynamicWorkflowBuilder(INodeHandler handler) => _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        internal IReadOnlyCollection<string> InputKeys => _inputKeys;

        public DynamicWorkflowBuilder WithValidation(bool validate) { _validateOnBuild = validate; return this; }
        public DynamicChainBuilder CreateChain(string chainName = null) => new(this, _handler, chainName);

        internal bool AddNode(object node, IEnumerable<string> resultKeys, string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId))
            {
                if (!_nodeIds.Add(nodeId))
                    throw new InvalidOperationException(string.Format(ExceptionMessages.DuplicateNodeId, nodeId));
                _nodeIdToResultKeys[nodeId] = resultKeys.ToList();
                _nodes.Add(node);
                return true;
            }
            _nodes.Add(node);
            return false;
        }

        internal void AddInputKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException(ExceptionMessages.InputKeyCannotBeNullOrEmpty);
            _inputKeys.Add(key);
        }

        internal bool TryGetNodeOutputs(string nodeId, out List<string> outputs) => _nodeIdToResultKeys.TryGetValue(nodeId, out outputs);
        internal IReadOnlyList<object> GetNodes() => _nodes.AsReadOnly();

        public List<object> Build(bool stripNodeIds = true)
        {
            if (_validateOnBuild) WorkflowValidator.Validate(_nodes, InputKeys, _handler);
            if (stripNodeIds)
            {
                foreach (var node in _nodes.OfType<IDictionary<string, object>>())
                {
                    if (node.ContainsKey("NodeId"))
                        node.Remove("NodeId");
                }
            }
            return _nodes;
        }

        public string BuildJson(bool format = true) =>
            JsonSerializer.Serialize(Build(true), new JsonSerializerOptions { WriteIndented = format, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public class DynamicChainBuilder
    {
        private readonly DynamicWorkflowBuilder _parent;
        private readonly INodeHandler _handler;
        private readonly string _chainName;
        private List<string> _previousOutputKeys = new();

        private IReadOnlyList<object> _cachedNodes;
        private IReadOnlyList<IReadOnlyList<string>> _cachedNodeOutputs;

        public DynamicChainBuilder(DynamicWorkflowBuilder parent, INodeHandler handler, string chainName)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _chainName = chainName;
        }

        public DynamicChainBuilder Then(object node)
        {
            var nodeCopy = _handler.CloneNode(node);
            var rawParams = _handler.GetParameterKeys(nodeCopy)?.ToList() ?? new List<object>();
            var context = CreateResolutionContext();
            var resolvedParams = ResolveParameters(rawParams, context, nodeCopy);

            KeyValidation.EnsureNoNullOrEmpty(resolvedParams, "Parameter", GetDisplayName(nodeCopy));
            _handler.SetParameterKeys(nodeCopy, resolvedParams.Cast<object>());

            var resultKeys = _handler.GetResultKeys(nodeCopy)?.ToList() ?? throw new ArgumentException(FormatMessage(ExceptionMessages.NodeMustDefineResultKeys));
            ValidateResultKeys(resultKeys, nodeCopy);

            string nodeId = _handler.GetNodeId(nodeCopy);
            _parent.AddNode(nodeCopy, resultKeys, nodeId);
            _previousOutputKeys = resultKeys;
            return this;
        }

        public DynamicWorkflowBuilder EndChain() => _parent;

        private string FormatMessage(string msg) => string.IsNullOrEmpty(_chainName) ? msg : $"[Chain '{_chainName}'] {msg}";
        private string GetDisplayName(object node) => _handler.GetDisplayName(node) ?? "unknown";

        private IResolutionContext CreateResolutionContext()
        {
            _cachedNodes = _parent.GetNodes();
            _cachedNodeOutputs = _cachedNodes.Select(n => (IReadOnlyList<string>)(_handler.GetResultKeys(n)?.ToList() ?? new List<string>())).ToList();

            return new ResolutionContext(
                _previousOutputKeys,
                _parent.InputKeys,
                id => _parent.TryGetNodeOutputs(id, out var list) ? list : throw new KeyNotFoundException(string.Format(ExceptionMessages.NodeIdNotFound, id)),
                idx =>
                {
                    int total = _cachedNodes.Count;
                    int nodeIdx = idx < 0 ? total + idx : idx;
                    if (nodeIdx < 0 || nodeIdx >= total)
                        throw new ArgumentOutOfRangeException(nameof(idx), $"Node index {idx} out of range. Total nodes: {total}");
                    return _cachedNodeOutputs[nodeIdx];
                });
        }

        private List<string> ResolveParameters(List<object> rawParams, IResolutionContext context, object currentNode)
        {
            var resolved = new List<string>();
            for (int i = 0; i < rawParams.Count; i++)
            {
                try
                {
                    resolved.Add(ResolveParameter(rawParams[i], context, currentNode));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(FormatMessage($"Error resolving parameter at index {i} in node '{GetDisplayName(currentNode)}': {ex.Message}"), ex);
                }
            }
            return resolved;
        }

        private string ResolveParameter(object param, IResolutionContext context, object currentNode)
        {
            switch (param)
            {
                case string s:
                    return s;
                case IPlaceholder p:
                    string resolvedKey = PlaceholderResolver.Resolve(p, context);
                    if (p is Placeholder { Kind: PlaceholderKind.Input, Key: var key } && !string.IsNullOrEmpty(key))
                        _parent.AddInputKey(key);
                    return resolvedKey;
                default:
                    throw new ArgumentException(string.Format(ExceptionMessages.ParameterMustBeStringOrPlaceholder, param?.GetType()) + $" Node: {GetDisplayName(currentNode)}");
            }
        }

        private void ValidateResultKeys(List<string> resultKeys, object node)
        {
            KeyValidation.EnsureNoNullOrEmpty(resultKeys, "Result", GetDisplayName(node));
            if (resultKeys.Count != resultKeys.Distinct().Count())
                throw new ArgumentException(string.Format(ExceptionMessages.OutputKeysContainDuplicates, GetDisplayName(node)));
        }

        private record ResolutionContext(
            IReadOnlyList<string> PreviousOutputs,
            IReadOnlyCollection<string> InputKeys,
            Func<string, IReadOnlyList<string>> GetNodeOutputsById,
            Func<int, IReadOnlyList<string>> GetNodeOutputsByIndex) : IResolutionContext
        {
            public IReadOnlyList<string> GetNodeOutputs(string nodeId) => GetNodeOutputsById(nodeId);
            public IReadOnlyList<string> GetNodeOutputs(int index) => GetNodeOutputsByIndex(index);
        }
    }
}

#region ChainGraph.Core

namespace ChainGraph.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using GraphFoundation.Abstractions;

    public static class ReferencePrefix
    {
        public const string Input = "@input.";
        public const string Node = "@node.";
        public const string Prev = "@prev";
        public const string AtSymbol = "@";
    }

    public static class NodeKeyExtensions
    {
        public static string GetDefaultOutputKey<TKey>(string symbol, TKey key) => $"{symbol}_{key}";
    }

    public static class ClonerFactory
    {
        private static readonly JsonSerializerOptions DefaultOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static IObjectCloner<T> CreateJsonCloner<T>() where T : class =>
            new JsonCloner<T>(DefaultOptions);

        public static JsonSerializerOptions CreateJsonClonerOptions() => DefaultOptions;
    }

    public class ParameterDefinition
    {
        public string Name { get; set; } = string.Empty;
        public Type Type { get; set; }
        public object DefaultValue { get; set; }
        public bool IsRequired { get; set; } = true;
        public string Description { get; set; }
    }

    public class ChainTemplate<TNode> where TNode : class
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; }
        public string Version { get; set; } = "1.0";
        public Dictionary<string, ParameterDefinition> InputParameters { get; set; } = new();
        public Dictionary<string, string> Outputs { get; set; } = new();
        public IList<TNode> Nodes { get; set; } = new List<TNode>();
        public Dictionary<TNode, string> OriginalKeys { get; set; } = new();
        public Func<TemplateInstantiationContext<TNode>, IList<TNode>> NodeBuilder { get; set; }

        public virtual bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                error = "Template name is required.";
                return false;
            }
            if (NodeBuilder == null && (Nodes == null || Nodes.Count == 0))
            {
                error = "Template must have either Nodes or a NodeBuilder.";
                return false;
            }

            foreach (var kv in InputParameters)
            {
                if (string.IsNullOrWhiteSpace(kv.Value.Name))
                {
                    error = $"Parameter key '{kv.Key}' has an empty or whitespace Name.";
                    return false;
                }
            }

            if (NodeBuilder == null)
            {
                var nodes = Nodes ?? new List<TNode>();
                var nodeSet = new HashSet<TNode>(nodes);
                foreach (var node in nodes)
                {
                    if (!OriginalKeys.TryGetValue(node, out var originalKey))
                    {
                        error = $"Node {node} has no original key mapping.";
                        return false;
                    }
                    if (string.IsNullOrEmpty(originalKey))
                    {
                        error = $"Node '{node}' has null or empty original key.";
                        return false;
                    }
                }
                foreach (var kv in OriginalKeys)
                {
                    if (!nodeSet.Contains(kv.Key))
                    {
                        error = $"OriginalKeys contains entry for node not in Nodes list.";
                        return false;
                    }
                }
            }
            else
            {
                foreach (var kv in OriginalKeys)
                {
                    if (string.IsNullOrEmpty(kv.Value))
                    {
                        error = $"Node '{kv.Key}' has null or empty original key.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }
    }

    public class ChainTemplateRegistry<TNode> where TNode : class
    {
        private readonly Dictionary<string, ChainTemplate<TNode>> _templates = new();

        public void Register(ChainTemplate<TNode> template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (!template.IsValid(out var error))
                throw new InvalidOperationException($"Template '{template.Name}' is invalid: {error}");
            _templates[template.Name] = template;
        }

        public bool TryGet(string name, out ChainTemplate<TNode> template) => _templates.TryGetValue(name, out template);
        public ChainTemplate<TNode> Get(string name) => _templates[name];
    }

    public class ChainInstanceReference
    {
        public string InstanceKey { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public Dictionary<string, object> ParameterBindings { get; set; } = new();
    }

    public class DataFlowGraphWithChains : GraphBuild.FromGraph.DataFlowGraph
    {
        public List<ChainInstanceReference> ChainInstances { get; set; } = new();
    }

    public class TemplateInstantiationContext<TNode> where TNode : class
    {
        public string InstanceKey { get; }
        public IReadOnlyDictionary<string, object> ParameterBindings { get; }
        public ChainTemplate<TNode> Template { get; }
        public IReferenceResolver<TNode> ReferenceResolver { get; }
        public IObjectCloner<TNode> NodeCloner { get; }
        public Func<TNode, string> KeyExtractor { get; }
        public Action<TNode, string> KeySetter { get; }

        private readonly Dictionary<string, object> _resolvedParameters = new();

        public TemplateInstantiationContext(
            string instanceKey,
            IReadOnlyDictionary<string, object> parameterBindings,
            ChainTemplate<TNode> template,
            IReferenceResolver<TNode> referenceResolver,
            IObjectCloner<TNode> nodeCloner,
            Func<TNode, string> keyExtractor,
            Action<TNode, string> keySetter)
        {
            InstanceKey = instanceKey;
            ParameterBindings = parameterBindings;
            Template = template;
            ReferenceResolver = referenceResolver;
            NodeCloner = nodeCloner;
            KeyExtractor = keyExtractor;
            KeySetter = keySetter;
        }

        public object ResolveParameter(string paramName)
        {
            if (_resolvedParameters.TryGetValue(paramName, out var cached))
                return cached;

            if (!ParameterBindings.TryGetValue(paramName, out var boundValue))
            {
                if (Template.InputParameters.TryGetValue(paramName, out var def) && def.DefaultValue != null)
                    boundValue = def.DefaultValue;
                else
                    throw new InvalidOperationException($"Parameter '{paramName}' is not bound and has no default value.");
            }

            var resolved = ReferenceResolver.Resolve(boundValue, this);
            _resolvedParameters[paramName] = resolved;
            return resolved;
        }

        public string ResolveInputPlaceholder(string paramName) =>
            ResolveParameter(paramName)?.ToString() ?? throw new InvalidOperationException($"Parameter '{paramName}' resolved to null.");

        public string GetNamespacePrefix() => $"{InstanceKey}_";
    }

    public interface IReferenceResolver<TNode> where TNode : class
    {
        object Resolve(object value, TemplateInstantiationContext<TNode> context);
        string ResolveNodeReference(string reference, TemplateInstantiationContext<TNode> context);
    }

    public static class PlaceholderParser
    {
        private static bool TryParseIndexFromBrackets(string expr, out int index, out string remaining)
        {
            index = -1;
            remaining = expr;
            int open = expr.IndexOf('[');
            if (open == -1) return false;

            int close = expr.IndexOf(']', open);
            if (close == -1) return false;

            string indexStr = expr.Substring(open + 1, close - open - 1);
            if (!int.TryParse(indexStr, out index)) return false;

            remaining = expr.Substring(close + 1);
            return true;
        }


        public static object ParseToParam(string expr)
        {
            if (string.IsNullOrEmpty(expr)) throw new ArgumentException("占位符表达式不能为空");

            if (expr == ReferencePrefix.Prev)
                return Placeholder.Prev();
            if (expr.StartsWith(ReferencePrefix.Prev + "["))
            {
                if (TryParseIndexFromBrackets(expr, out int idx, out string remaining))
                {
                    if (!string.IsNullOrEmpty(remaining))
                        throw new ArgumentException($"无效的占位符表达式: '{expr}' — 索引后有多余内容 '{remaining}'");
                    return Placeholder.PrevByIndex(idx);
                }
                throw new ArgumentException($"无效的索引格式: {expr}");
            }

            if (expr.StartsWith(ReferencePrefix.Prev + "."))
            {
                if (expr.Length <= (ReferencePrefix.Prev + ".").Length)
                    throw new ArgumentException($"无效的占位符表达式: {expr}");
                return Placeholder.PrevByKey(expr[(ReferencePrefix.Prev + ".").Length..]);
            }

            if (expr.StartsWith(ReferencePrefix.Input))
            {
                var key = expr[ReferencePrefix.Input.Length..];
                if (string.IsNullOrEmpty(key)) throw new ArgumentException($"无效的输入键: {expr}");
                return Placeholder.Input(key);
            }

            if (expr.StartsWith(ReferencePrefix.Node))
            {
                var rest = expr[ReferencePrefix.Node.Length..];
                if (rest.StartsWith('['))
                {
                    if (TryParseIndexFromBrackets(expr, out int idx, out string remaining))
                    {
                        if (!string.IsNullOrEmpty(remaining))
                            throw new ArgumentException($"无效的占位符表达式: '{expr}' — 索引后有多余内容 '{remaining}'");
                        return Placeholder.Node(idx);
                    }
                    throw new ArgumentException($"无效的节点索引: {expr}");
                }

                var dotIdx = rest.IndexOf('.');
                if (dotIdx > 0)
                {
                    var nodeKey = rest[..dotIdx];
                    var after = rest[(dotIdx + 1)..];
                    var bracketIdx = after.IndexOf('[');
                    if (bracketIdx > 0)
                    {
                        if (TryParseIndexFromBrackets(after, out int idx, out string remaining))
                        {
                            if (!string.IsNullOrEmpty(remaining))
                                throw new ArgumentException($"无效的占位符表达式: '{expr}' — 索引后有多余内容 '{remaining}'");
                            return Placeholder.From(nodeKey, idx);
                        }
                        throw new ArgumentException($"无效的索引格式: {after}");
                    }

                    return Placeholder.From(nodeKey, after);
                }
                return Placeholder.From(rest);
            }

            throw new NotSupportedException($"不支持的占位符表达式: {expr}");
        }

        public static bool IsInputReference(string value) =>
            value is string s && s.StartsWith(ReferencePrefix.Input);

        public static bool IsNodeReference(string value) =>
            value is string s && s.StartsWith(ReferencePrefix.Node);

        public static bool IsPrevReference(string value) =>
            value is string s && s.StartsWith(ReferencePrefix.Prev);

        public static string ExtractInputKey(string inputRef) =>
            inputRef[ReferencePrefix.Input.Length..];

        public static string ExtractNodePath(string nodeRef) =>
            nodeRef[ReferencePrefix.Node.Length..];
    }

    public class JsonCloner<T> : IObjectCloner<T> where T : class
    {
        private readonly JsonSerializerOptions _options;

        public JsonCloner(JsonSerializerOptions options = null)
        {
            _options = options ?? ClonerFactory.CreateJsonClonerOptions();
        }

        public T Clone(T source) =>
            source == null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source, _options), _options);
    }

    #region Internal

    namespace Internal
    {
        using Graphkey.Core;

        public static class TemplateInstantiatorCore
        {
            public static (IList<TNode> Nodes, Dictionary<string, string> OutputMappings) Instantiate<TNode>(
                ChainTemplate<TNode> template,
                string instanceKey,
                IReadOnlyDictionary<string, object> parameterBindings,
                IReferenceResolver<TNode> referenceResolver,
                IChainNodeHandler<TNode, ChainInfo<TNode>> handler,
                IObjectCloner<TNode> nodeCloner,
                Func<TNode, string> keyExtractor,
                Action<TNode, string> keySetter)
                where TNode : class
            {
                var context = new TemplateInstantiationContext<TNode>(
                    instanceKey, parameterBindings, template, referenceResolver,
                    nodeCloner, keyExtractor, keySetter);

                IList<TNode> sourceNodes;
                Dictionary<TNode, string> originalKeyMap;

                if (template.NodeBuilder != null)
                {
                    sourceNodes = template.NodeBuilder(context);
                    originalKeyMap = sourceNodes.ToDictionary(n => n, keyExtractor);
                    foreach (var kv in originalKeyMap)
                    {
                        if (string.IsNullOrEmpty(kv.Value))
                            throw new InvalidOperationException($"NodeBuilder returned a node with null or empty key for template '{template.Name}', instance '{instanceKey}'.");
                    }
                }
                else
                {
                    sourceNodes = template.Nodes;
                    originalKeyMap = template.OriginalKeys;
                }

                var prefix = context.GetNamespacePrefix();
                var resultNodes = new List<TNode>();

                foreach (var originalNode in sourceNodes)
                {
                    var cloned = nodeCloner.Clone(originalNode);
                    if (originalKeyMap.TryGetValue(originalNode, out var originalKey))
                        keySetter(cloned, prefix + originalKey);

                    handler.UpdateReferences(cloned, (oldRef, _) =>
                    {
                        if (string.IsNullOrEmpty(oldRef)) return oldRef;
                        if (PlaceholderParser.IsInputReference(oldRef))
                            return context.ResolveInputPlaceholder(PlaceholderParser.ExtractInputKey(oldRef));
                        if (PlaceholderParser.IsNodeReference(oldRef))
                            return referenceResolver.ResolveNodeReference(PlaceholderParser.ExtractNodePath(oldRef), context);
                        var resolved = referenceResolver.Resolve(oldRef, context);
                        return resolved?.ToString() ?? oldRef;
                    });
                    resultNodes.Add(cloned);
                }

                var outputMappings = template.Outputs.ToDictionary(kv => kv.Key, kv => prefix + kv.Value);
                return (resultNodes, outputMappings);
            }
        }

        public class GraphReferenceResolverCore<TNode> : IReferenceResolver<TNode> where TNode : class
        {
            protected readonly DataFlowGraphWithChains _graph;
            protected readonly Dictionary<string, Dictionary<string, string>> _outputMappings;

            public GraphReferenceResolverCore(DataFlowGraphWithChains graph, Dictionary<string, Dictionary<string, string>> outputMappings)
                => (_graph, _outputMappings) = (graph, outputMappings);

            public virtual object Resolve(object value, TemplateInstantiationContext<TNode> context)
            {
                if (value is not string s) return value;
                if (PlaceholderParser.IsInputReference(s) || PlaceholderParser.IsPrevReference(s))
                    return s;
                if (PlaceholderParser.IsNodeReference(s))
                    return ResolveNodeReference(PlaceholderParser.ExtractNodePath(s), context);
                return value;
            }

            public virtual string ResolveNodeReference(string reference, TemplateInstantiationContext<TNode> context)
            {
                if (string.IsNullOrEmpty(reference)) throw new ArgumentException("Node reference cannot be empty.");
                var dotIndex = reference.IndexOf('.');
                string targetKey = dotIndex > 0 ? reference.Substring(0, dotIndex) : reference;
                string outputName = dotIndex > 0 ? reference.Substring(dotIndex + 1) : null;

                string currentPrefix = context?.GetNamespacePrefix()?.TrimEnd('_');
                if (currentPrefix != null && targetKey.StartsWith(currentPrefix))
                    return reference;

                if (!_outputMappings.TryGetValue(targetKey, out var mappings))
                    throw new InvalidOperationException($"Referenced instance/node '{targetKey}' not found.");
                if (outputName == null)
                    return mappings.Values.FirstOrDefault() ?? throw new InvalidOperationException($"Instance '{targetKey}' has no output mappings.");
                if (!mappings.TryGetValue(outputName, out var resolvedKey))
                    throw new InvalidOperationException($"Output '{outputName}' not found in instance '{targetKey}'.");
                return resolvedKey;
            }
        }
    }

    #endregion

    namespace Expansion
    {
        public delegate TNode GraphNodeConverter<out TNode>(GraphBuild.FromGraph.GraphNode graphNode, Func<object, object> valueResolver) where TNode : class;

        public class ChainExpanderOptions<TNode> where TNode : class
        {
            public IChainNodeHandler<TNode, ChainInfo<TNode>> NodeHandler { get; set; } = null!;
            public GraphNodeConverter<TNode> NodeConverter { get; set; } = null!;
            public NormalizationConfig NormalizationConfig { get; set; }
            public IObjectCloner<TNode> NodeCloner { get; set; }
            public Func<TNode, string> KeyExtractor { get; set; }
            public Action<TNode, string> KeySetter { get; set; }
        }

        public class TemplateInstantiator<TNode> where TNode : class
        {
            private readonly IChainNodeHandler<TNode, ChainInfo<TNode>> _handler;
            private readonly IObjectCloner<TNode> _nodeCloner;
            public Func<TNode, string> KeyExtractor { get; }
            public Action<TNode, string> KeySetter { get; }

            public TemplateInstantiator(IChainNodeHandler<TNode, ChainInfo<TNode>> handler,
                IObjectCloner<TNode> nodeCloner,
                Func<TNode, string> keyExtractor, Action<TNode, string> keySetter)
            {
                _handler = handler;
                _nodeCloner = nodeCloner;
                KeyExtractor = keyExtractor;
                KeySetter = keySetter;
            }

            public (IList<TNode> Nodes, Dictionary<string, string> OutputMappings) Instantiate(
                ChainTemplate<TNode> template, string instanceKey,
                IReadOnlyDictionary<string, object> parameterBindings, IReferenceResolver<TNode> referenceResolver)
            {
                return Internal.TemplateInstantiatorCore.Instantiate(template, instanceKey, parameterBindings,
                    referenceResolver, _handler, _nodeCloner, KeyExtractor, KeySetter);
            }
        }

        public class NormalizationConfig
        {
            public string RenamePattern { get; set; } = "{0}_{1}";
            public int MaxDuplicates { get; set; } = 100;
        }

        public class NodeLocation
        {
            public string Key { get; set; }
            public int ChainIndex { get; set; }
            public int NodeIndex { get; set; }
        }
    }

    public class ChainInfo<TNode> where TNode : class
    {
        public string ChainId { get; set; } = string.Empty;
        public IList<string> NodeKeys { get; set; } = new List<string>();
        public IList<string> EdgeIds { get; set; } = new List<string>();
        public object? UserData { get; set; }
        public TNode? StartNode { get; set; }
        public TNode? EndNode { get; set; }
        public IList<TNode?> AllNodes { get; set; } = new List<TNode?>();
    }

    public class LinearChainAdapter<TNodeData, TChain> : IGraphModelAdapter<TChain, string, TNodeData, object>
        where TNodeData : class where TChain : class
    {
        private readonly Func<TChain, IList<TNodeData?>> _getNodes;
        private readonly Action<TChain, IList<TNodeData?>> _setNodes;
        private readonly Func<TChain, TNodeData> _getStartNode;
        private readonly Action<TChain, TNodeData> _setStartNode;
        private readonly Func<TChain, TNodeData> _getEndNode;
        private readonly Action<TChain, TNodeData> _setEndNode;
        private readonly Func<TChain, TChain> _cloneChain;
        private readonly IObjectCloner<TNodeData> _nodeCloner;
        private readonly Func<TChain, int, int, string> _keyGenerator;
        private readonly Dictionary<int, List<NodeLocation>> _chainLocations = new();
        private readonly Dictionary<int, TChain> _originalChains = new();
        private readonly Dictionary<string, TNodeData> _originalNodeByKey = new();

        public LinearChainAdapter(
            Func<TChain, IList<TNodeData?>> getNodes, Action<TChain, IList<TNodeData?>> setNodes,
            Func<TChain, TNodeData> getStartNode, Action<TChain, TNodeData> setStartNode,
            Func<TChain, TNodeData> getEndNode, Action<TChain, TNodeData> setEndNode,
            Func<TChain, TChain> cloneChain, IObjectCloner<TNodeData>? nodeCloner = null,
            Func<TChain, int, int, string>? keyGenerator = null)
        {
            _getNodes = getNodes ?? throw new ArgumentNullException(nameof(getNodes));
            _setNodes = setNodes ?? throw new ArgumentNullException(nameof(setNodes));
            _getStartNode = getStartNode ?? throw new ArgumentNullException(nameof(getStartNode));
            _setStartNode = setStartNode ?? throw new ArgumentNullException(nameof(setStartNode));
            _getEndNode = getEndNode ?? throw new ArgumentNullException(nameof(getEndNode));
            _setEndNode = setEndNode ?? throw new ArgumentNullException(nameof(setEndNode));
            _cloneChain = cloneChain ?? throw new ArgumentNullException(nameof(cloneChain));
            _nodeCloner = nodeCloner ?? new JsonCloner<TNodeData>();
            _keyGenerator = keyGenerator ?? ((chain, chainIdx, nodeIdx) => $"chain_{chainIdx}_node_{nodeIdx}");
        }

        public (ICoreGraph<string, TNodeData, object> Graph, IReadOnlyList<NodeLocation> Locations) ConvertToGraph(IEnumerable<TChain> models)
        {
            _chainLocations.Clear(); _originalChains.Clear(); _originalNodeByKey.Clear();
            var graph = new SimpleCoreGraph<string, TNodeData, object>();
            var locations = new List<NodeLocation>();
            int chainIndex = 0;
            foreach (var chain in models)
            {
                if (chain == null) continue;
                var nodes = _getNodes(chain);
                if (nodes == null) continue;
                var chainLocationList = new List<NodeLocation>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node == null) continue;
                    var key = _keyGenerator(chain, chainIndex, i);
                    graph.AddNode(key, node, null);
                    var loc = new NodeLocation(key, chainIndex, i, node);
                    locations.Add(loc);
                    chainLocationList.Add(loc);
                    _originalNodeByKey[key] = node;
                }
                _chainLocations[chainIndex] = chainLocationList;
                _originalChains[chainIndex] = chain;
                chainIndex++;
            }
            return (graph, locations);
        }

        public TChain ConvertFromGraph(ICoreGraph<string, TNodeData, object> graph, NodeLocation rootLocation, object? context)
        {
            int chainIdx = rootLocation.ChainIndex;
            if (!_originalChains.TryGetValue(chainIdx, out var originalChain))
                throw new InvalidOperationException($"No original chain found for index {chainIdx}.");
            var chain = _cloneChain(originalChain);
            if (!_chainLocations.TryGetValue(chainIdx, out var locations) || locations == null || locations.Count == 0)
            {
                _setNodes(chain, new List<TNodeData?>());
                return chain;
            }
            var chainNodes = locations.OrderBy(l => l.NodeIndex).Select(loc => GetClonedNodeData(graph.GetNodeData(loc.Key), loc.Key)).ToList();
            _setNodes(chain, chainNodes);
            if (chainNodes.Count > 0)
            {
                var firstNode = chainNodes.FirstOrDefault(n => n != null);
                var lastNode = chainNodes.LastOrDefault(n => n != null);
                if (firstNode != null) _setStartNode(chain, firstNode);
                if (lastNode != null) _setEndNode(chain, lastNode);
            }
            return chain;
        }

        private TNodeData? GetClonedNodeData(TNodeData? nodeData, string key) =>
            nodeData != null ? _nodeCloner.Clone(nodeData) : _originalNodeByKey.TryGetValue(key, out var originalNode) ? _nodeCloner.Clone(originalNode) : null;
    }

    public record NodeLocation(string Key, int ChainIndex = -1, int NodeIndex = -1, object? UserData = null);

    public interface IGraphModelAdapter<TModel, TKey, TNodeData, TEdgeData>
        where TKey : notnull where TNodeData : class
    {
        (ICoreGraph<TKey, TNodeData, TEdgeData> Graph, IReadOnlyList<NodeLocation> Locations) ConvertToGraph(IEnumerable<TModel> models);
        TModel ConvertFromGraph(ICoreGraph<TKey, TNodeData, TEdgeData> graph, NodeLocation rootLocation, object? context);
    }

    public class SimpleCoreGraph<TKey, TNodeData, TEdgeData> : ICoreGraph<TKey, TNodeData, TEdgeData>
        where TKey : notnull where TNodeData : class
    {
        private readonly Dictionary<TKey, (TNodeData Data, string? Symbol)> _nodes = new();
        private readonly Dictionary<string, (TKey Source, TKey Target, bool IsDirected, TEdgeData Data, double Weight)> _edges = new();

        public void AddNode(TKey key, TNodeData data, string? symbol)
        {
            _nodes[key] = (data, symbol);
        }

        public bool ContainsNode(TKey key) => _nodes.ContainsKey(key);

        public TNodeData? GetNodeData(TKey key) =>
            _nodes.TryGetValue(key, out var entry) ? entry.Data : default;

        public string? GetNodeSymbol(TKey key) =>
            _nodes.TryGetValue(key, out var entry) ? entry.Symbol : null;

        public IReadOnlyList<TKey> GetAllNodeKeys() => _nodes.Keys.ToList();

        public void UpdateNode(TKey key, TNodeData newData, string? newSymbol)
        {
            if (_nodes.ContainsKey(key))
                _nodes[key] = (newData, newSymbol);
        }

        public void RemoveNode(TKey key) => _nodes.Remove(key);

        public void AddEdge(string edgeId, TKey source, TKey target, bool isDirected, TEdgeData? data, double weight)
        {
            _edges[edgeId] = (source, target, isDirected, data, weight);
        }

        public bool ContainsEdge(string edgeId) => _edges.ContainsKey(edgeId);

        public TEdgeData? GetEdgeData(string edgeId) =>
            _edges.TryGetValue(edgeId, out var entry) ? entry.Data : default;

        public double GetEdgeWeight(string edgeId) =>
            _edges.TryGetValue(edgeId, out var entry) ? entry.Weight : 0;

        public TKey GetEdgeSource(string edgeId) =>
            _edges.TryGetValue(edgeId, out var entry) ? entry.Source : throw new KeyNotFoundException($"Edge '{edgeId}' not found.");

        public TKey GetEdgeTarget(string edgeId) =>
            _edges.TryGetValue(edgeId, out var entry) ? entry.Target : throw new KeyNotFoundException($"Edge '{edgeId}' not found.");

        public IReadOnlyList<string> GetAllEdgeIds() => _edges.Keys.ToList();

        public void RemoveEdge(string edgeId) => _edges.Remove(edgeId);

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetOutgoingEdges(TKey source)
        {
            var result = new List<(TKey, TKey, string, bool, double, TEdgeData)>();
            foreach (var (edgeId, (src, tgt, isDir, data, weight)) in _edges)
            {
                if (EqualityComparer<TKey>.Default.Equals(src, source))
                    result.Add((src, tgt, edgeId, isDir, weight, data));
            }
            return result;
        }

        public IReadOnlyList<(TKey Source, TKey Target, string EdgeId, bool IsDirected, double Weight, TEdgeData Data)> GetIncomingEdges(TKey target)
        {
            var result = new List<(TKey, TKey, string, bool, double, TEdgeData)>();
            foreach (var (edgeId, (src, tgt, isDir, data, weight)) in _edges)
            {
                if (EqualityComparer<TKey>.Default.Equals(tgt, target))
                    result.Add((src, tgt, edgeId, isDir, weight, data));
            }
            return result;
        }

        public IReadOnlyList<TKey> GetNeighbors(TKey key)
        {
            var neighbors = new HashSet<TKey>();
            foreach (var (edgeId, (src, tgt, isDir, data, weight)) in _edges)
            {
                if (EqualityComparer<TKey>.Default.Equals(src, key))
                    neighbors.Add(tgt);
                if (!isDir && EqualityComparer<TKey>.Default.Equals(tgt, key))
                    neighbors.Add(src);
            }
            return neighbors.ToList();
        }

        public bool GetEdgeIsDirected(string edgeId) =>
            _edges.TryGetValue(edgeId, out var entry) ? entry.IsDirected : throw new KeyNotFoundException($"Edge '{edgeId}' not found.");

        public IObservable<GraphEvent> Events => throw new NotSupportedException("SimpleCoreGraph does not support Events.");
        public ITransaction BeginTransaction() => throw new NotSupportedException("SimpleCoreGraph does not support transactions.");

        public void Dispose()
        {
            _nodes.Clear();
            _edges.Clear();
        }
    }

    public class ChainTemplateExpander<TNode> where TNode : class
    {
        private readonly ChainTemplateRegistry<TNode> _registry;
        private readonly Expansion.TemplateInstantiator<TNode> _instantiator;
        private readonly IChainNodeHandler<TNode, ChainInfo<TNode>> _handler;
        private readonly Expansion.GraphNodeConverter<TNode> _nodeConverter;
        private readonly LinearChainAdapter<TNode, ChainInfo<TNode>> _adapter;
        private readonly Expansion.NormalizationConfig _normalizationConfig;
        private readonly Dictionary<string, Dictionary<string, string>> _instanceOutputMappings = new();

        public ChainTemplateExpander(Expansion.ChainExpanderOptions<TNode> options, ChainTemplateRegistry<TNode> registry)
        {
            _handler = options.NodeHandler ?? throw new ArgumentNullException(nameof(options.NodeHandler));
            _nodeConverter = options.NodeConverter ?? throw new ArgumentNullException(nameof(options.NodeConverter));
            _normalizationConfig = options.NormalizationConfig ?? new Expansion.NormalizationConfig();
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            var cloner = options.NodeCloner;
            var keyExtractor = options.KeyExtractor ?? (node => _handler.GetKey(node) ?? string.Empty);
            var keySetter = options.KeySetter ?? ((node, key) => _handler.SetKey(node, key));
            _instantiator = new Expansion.TemplateInstantiator<TNode>(_handler, cloner, keyExtractor, keySetter);

            _adapter = new LinearChainAdapter<TNode, ChainInfo<TNode>>(
                getNodes: c => _handler.GetNodes(c) ?? new List<TNode>(),
                setNodes: (c, nodes) => _handler.SetNodes(c, nodes!),
                getStartNode: c => _handler.GetStartNode(c) ?? throw new InvalidOperationException("StartNode is null"),
                setStartNode: (c, n) => _handler.SetStartNode(c, n),
                getEndNode: c => _handler.GetEndNode(c) ?? throw new InvalidOperationException("EndNode is null"),
                setEndNode: (c, n) => _handler.SetEndNode(c, n),
                cloneChain: c => _handler.CloneChain(c),
                nodeCloner: cloner,
                keyGenerator: (chain, chainIdx, nodeIdx) => $"{chain.ChainId}_node_{nodeIdx}");
        }

        private List<ChainInfo<TNode>> ExpandToChains(DataFlowGraphWithChains graph)
        {
            _instanceOutputMappings.Clear();
            var result = new List<ChainInfo<TNode>>();
            var resolver = new Internal.GraphReferenceResolverCore<TNode>(graph, _instanceOutputMappings);

            foreach (var node in graph.Nodes)
            {
                var nodeKey = node.Key?.ToString() ?? throw new InvalidOperationException("GraphNode must have a Key.");
                var nodeData = _nodeConverter(node, val => ResolveGraphNodeValue(val, resolver));
                result.Add(new ChainInfo<TNode> { ChainId = nodeKey, AllNodes = new List<TNode> { nodeData }, StartNode = nodeData, EndNode = nodeData });
                _instanceOutputMappings[nodeKey] = (node.OutputKeys ?? new List<string>()).ToDictionary(o => o, o => o);
            }

            foreach (var instance in graph.ChainInstances)
            {
                if (!_registry.TryGet(instance.TemplateName, out var template))
                    throw new InvalidOperationException($"Template '{instance.TemplateName}' not found.");
                var (nodes, outputs) = _instantiator.Instantiate(template, instance.InstanceKey, instance.ParameterBindings, resolver);
                result.Add(new ChainInfo<TNode> { ChainId = instance.InstanceKey, AllNodes = nodes, StartNode = nodes.FirstOrDefault(), EndNode = nodes.LastOrDefault() });
                _instanceOutputMappings[instance.InstanceKey] = outputs;
            }
            return result;
        }

        public List<object> ExpandToWorkflowNodes(DataFlowGraphWithChains graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var chains = ExpandToChains(graph);
            var (coreGraph, locations) = _adapter.ConvertToGraph(chains);
            var workflow = new List<object>();
            foreach (var loc in locations.OrderBy(l => l.ChainIndex).ThenBy(l => l.NodeIndex))
            {
                var data = coreGraph.GetNodeData(loc.Key);
                if (data != null) workflow.Add(data);
            }
            return workflow;
        }

        private static object ResolveGraphNodeValue(object val, Internal.GraphReferenceResolverCore<TNode> resolver)
        {
            if (val is string s)
            {
                if (PlaceholderParser.IsInputReference(s))
                    return PlaceholderParser.ExtractInputKey(s);
                return resolver.Resolve(s, null);
            }
            return val;
        }
    }
}

#endregion

#region GraphBuild.FromGraph

namespace GraphBuild.FromGraph
{
    using ChainGraph.Core;
    using Graphkey.Core;
    using System;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Linq;

    public class GraphNode
    {
        public string Key { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        [Obsolete("FuncName is redundant; use Symbol instead.")]
        public string FuncName { get; set; }
        public List<string> OutputKeys { get; set; } = new();
        public Dictionary<string, object> Config { get; set; } = new();
    }

    public class GraphEdge
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string SourceOutputKey { get; set; }
        public int? TargetParameterIndex { get; set; }
        public double Weight { get; set; } = 1.0;
    }

    public class DataFlowGraph
    {
        public List<GraphNode> Nodes { get; set; } = new();
        public List<GraphEdge> Edges { get; set; } = new();
        public List<string> InputKeys { get; set; } = new();
        public string Name { get; set; }
    }

    public static class GraphWorkflowBuilder
    {
        public static List<object> BuildFromGraph(DataFlowGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var sortedKeys = graph.TopologicalSort();
            var nodeMap = graph.Nodes.ToDictionary(n => n.Key);
            var outputKeyMap = graph.Nodes.ToDictionary(
                n => n.Key,
                n => n.OutputKeys.Count > 0 ? n.OutputKeys.ToList() : new List<string> { NodeKeyExtensions.GetDefaultOutputKey(n.Symbol, n.Key) });

            var edgesByTarget = graph.Edges.Select((e, idx) => new { Edge = e, Index = idx })
                    .GroupBy(item => item.Edge.Target)
                    .ToDictionary(g => g.Key, g => g.OrderBy(item => item.Edge.TargetParameterIndex ?? int.MaxValue).ThenBy(item => item.Index).Select(item => item.Edge).ToList());
            var paramListMap = new Dictionary<string, List<object>>();

            foreach (var key in sortedKeys)
            {
                var paramList = new List<object>();
                if (edgesByTarget.TryGetValue(key, out var incoming))
                {
                    foreach (var edge in incoming)
                    {
                        if (!outputKeyMap.TryGetValue(edge.Source, out var srcOut))
                            throw new InvalidOperationException($"源节点 {edge.Source} 未找到或无输出");
                        string srcKey = edge.SourceOutputKey ?? srcOut.FirstOrDefault() ?? throw new InvalidOperationException($"源节点 {edge.Source} 没有输出键");
                        paramList.Add(Placeholder.From(edge.Source, srcKey));
                    }
                }

                foreach (var kv in nodeMap[key].Config)
                {
                    object resolved = kv.Value switch
                    {
                        string s when s.StartsWith(ReferencePrefix.Input) => PlaceholderParser.ParseToParam(s),
                        string s when s.StartsWith(ReferencePrefix.AtSymbol) => PlaceholderParser.ParseToParam(s),
                        _ => kv.Value
                    };
                    paramList.Add(resolved);
                }
                paramListMap[key] = paramList;
            }

            var workflowNodes = new List<object>();
            foreach (var key in sortedKeys)
            {
                var node = nodeMap[key];
                dynamic expando = new ExpandoObject();
                expando.Symbol = node.Symbol;
                expando.Label = node.Label;
                expando.NodeId = key;
                dynamic config = new ExpandoObject();
                config.FuncName = node.Symbol;
                config.ParameterKeys = paramListMap[key].ToArray();
                config.ResultKeys = outputKeyMap[key].ToArray();
                expando.FuncPipelineConfig = config;
                workflowNodes.Add(expando);
            }
            return workflowNodes;
        }

        public static List<string> TopologicalSort(this DataFlowGraph graph)
        {
            var inDegree = graph.Nodes.ToDictionary(n => n.Key, _ => 0);
            var adj = graph.Nodes.ToDictionary(n => n.Key, _ => new List<string>());
            foreach (var e in graph.Edges)
            {
                if (!inDegree.ContainsKey(e.Source) || !inDegree.ContainsKey(e.Target))
                    throw new InvalidOperationException($"边引用了未知节点: {e.Source} -> {e.Target}");
                inDegree[e.Target]++;
                adj[e.Source].Add(e.Target);
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var result = new List<string>();
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                result.Add(u);
                foreach (var v in adj[u])
                    if (--inDegree[v] == 0) queue.Enqueue(v);
            }
            if (result.Count != graph.Nodes.Count) throw new InvalidOperationException("图中存在环，无法拓扑排序。");
            return result;
        }
    }

    public static class DataFlowGraphExtensions
    {
        public static void Merge(this DataFlowGraph target, DataFlowGraph source)
        {
            var existingNodeKeys = new HashSet<string>(target.Nodes.Select(n => n.Key));
            foreach (var node in source.Nodes)
            {
                if (!existingNodeKeys.Add(node.Key))
                    throw new InvalidOperationException($"Cannot merge: node key '{node.Key}' already exists in target graph.");
                target.Nodes.Add(node);
            }
            target.Edges.AddRange(source.Edges);
            foreach (var ik in source.InputKeys)
                if (!target.InputKeys.Contains(ik))
                    target.InputKeys.Add(ik);
        }
    }
}

#endregion

#region GraphConstruction

namespace GraphConstruction
{
    using ChainGraph.Core;
    using ChainGraph.Core.Expansion;
    using GraphBuild.FromGraph;
    using Graphkey.Core;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    internal class NodeCollectionManager<TNode>
    {
        private readonly Dictionary<string, TNode> _map = new();
        private readonly Func<TNode, string> _keySelector;
        private readonly Func<TNode, string> _symbolSelector;
        private readonly Action<TNode, List<string>> _outputKeysSetter;

        public NodeCollectionManager(Func<TNode, string> keySelector, Func<TNode, string> symbolSelector, Action<TNode, List<string>> outputKeysSetter)
        {
            _keySelector = keySelector;
            _symbolSelector = symbolSelector;
            _outputKeysSetter = outputKeysSetter;
        }

        public void Add(TNode node)
        {
            var key = _keySelector(node);
            if (_map.ContainsKey(key))
                throw new InvalidOperationException($"Node key '{key}' already exists.");
            _map[key] = node;

            if (node is GraphNode gn && (gn.OutputKeys == null || gn.OutputKeys.Count == 0))
            {
                var symbol = _symbolSelector(node);
                gn.OutputKeys = new List<string> { NodeKeyExtensions.GetDefaultOutputKey(symbol, key) };
            }
        }

        public bool ContainsKey(string key) => _map.ContainsKey(key);
        public TNode Get(string key) => _map[key];
        public IEnumerable<TNode> All => _map.Values;
    }

    public class GraphBuilder
    {
        private readonly DataFlowGraphWithChains _graph = new();
        private readonly ChainTemplateRegistry<GraphNode> _templateRegistry = new();
        private readonly NodeCollectionManager<GraphNode> _nodeManager;

        private GraphBuilder(string graphName = null)
        {
            _graph.Name = graphName;
            _nodeManager = new NodeCollectionManager<GraphNode>(
                n => n.Key,
                n => n.Symbol,
                (n, list) => n.OutputKeys = list);
        }

        public static GraphBuilder Create(string graphName = null) => new(graphName);

        public GraphBuilder AddInput(params string[] keys)
        {
            foreach (var k in keys) if (!_graph.InputKeys.Contains(k)) _graph.InputKeys.Add(k);
            return this;
        }

        public GraphBuilder AddNode(string key, string symbol, Action<GraphNode> configure = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Node key cannot be null or empty.");
            var node = new GraphNode { Key = key, Symbol = symbol, Label = symbol };
            configure?.Invoke(node);
            _nodeManager.Add(node);
            _graph.Nodes.Add(node);
            return this;
        }

        public GraphBuilder AddEdge(string sourceKey, string targetKey, string sourceOutputKey = null, int targetParamIndex = 0)
        {
            if (!_nodeManager.ContainsKey(sourceKey)) throw new ArgumentException($"Source node '{sourceKey}' not found.");
            if (!_nodeManager.ContainsKey(targetKey)) throw new ArgumentException($"Target node '{targetKey}' not found.");
            _graph.Edges.Add(new GraphEdge { Source = sourceKey, Target = targetKey, SourceOutputKey = sourceOutputKey, TargetParameterIndex = targetParamIndex });
            return this;
        }

        public GraphBuilder RegisterTemplate(ChainTemplate<GraphNode> template)
        {
            _templateRegistry.Register(template);
            return this;
        }

        public GraphBuilder AddTemplateInstance(string instanceKey, string templateName, object parameterBindings = null)
        {
            var bindings = parameterBindings?.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(parameterBindings)) ?? new();
            _graph.ChainInstances.Add(new ChainInstanceReference { InstanceKey = instanceKey, TemplateName = templateName, ParameterBindings = bindings });
            return this;
        }

        public List<object> Build()
        {
            var expander = new ChainTemplateExpander<GraphNode>(new ChainExpanderOptions<GraphNode>
            {
                NodeHandler = new GraphNodeChainHandler(),
                NodeConverter = (gn, resolver) =>
                {
                    var cloned = ClonerFactory.CreateJsonCloner<GraphNode>().Clone(gn);
                    if (cloned.Config != null)
                        foreach (var k in cloned.Config.Keys.ToList())
                            if (cloned.Config[k] is string s)
                                cloned.Config[k] = resolver(s);
                    return cloned;
                },
                NodeCloner = (IObjectCloner<GraphNode>)ClonerFactory.CreateJsonCloner<GraphNode>(),
                KeyExtractor = n => n.Key,
                KeySetter = (n, k) => n.Key = k,
                NormalizationConfig = new NormalizationConfig()
            }, _templateRegistry);

            return expander.ExpandToWorkflowNodes(_graph);
        }

        private class GraphNodeChainHandler : IChainNodeHandler<GraphNode, ChainInfo<GraphNode>>
        {
            public string GetKey(GraphNode node) => node.Key;
            public void SetKey(GraphNode node, string key) => node.Key = key;
            public ChainInfo<GraphNode> CloneChain(ChainInfo<GraphNode> chain)
                => new() { ChainId = chain.ChainId, AllNodes = chain.AllNodes.ToList(), StartNode = chain.StartNode, EndNode = chain.EndNode };
            public IList<GraphNode> GetNodes(ChainInfo<GraphNode> chain) => chain.AllNodes;
            public void SetNodes(ChainInfo<GraphNode> chain, IList<GraphNode> nodes) => chain.AllNodes = nodes;
            public GraphNode GetStartNode(ChainInfo<GraphNode> chain) => chain.StartNode;
            public void SetStartNode(ChainInfo<GraphNode> chain, GraphNode node) => chain.StartNode = node;
            public GraphNode GetEndNode(ChainInfo<GraphNode> chain) => chain.EndNode;
            public void SetEndNode(ChainInfo<GraphNode> chain, GraphNode node) => chain.EndNode = node;
            public void UpdateReferences(GraphNode node, Func<string, GraphNode, string> resolve) { }
            public string GetSymbol(GraphNode node) => node.Symbol;
            public void SetSymbol(GraphNode node, string symbol) => node.Symbol = symbol;
            public IEnumerable<string> GetAdditionalSymbols(GraphNode node) => Enumerable.Empty<string>();
            public void SetAdditionalSymbols(GraphNode node, IEnumerable<string> symbols) { }
            public GraphNode CloneNode(GraphNode node) => ClonerFactory.CreateJsonCloner<GraphNode>().Clone(node);
        }
    }

    public class TemplateBuilder
    {
        private readonly ChainTemplate<GraphNode> _template = new();
        private readonly NodeCollectionManager<GraphNode> _nodeManager;

        public TemplateBuilder(string name)
        {
            _template.Name = name;
            _nodeManager = new NodeCollectionManager<GraphNode>(
                n => n.Key,
                n => n.Symbol,
                (n, list) => n.OutputKeys = list);
        }

        public TemplateBuilder WithDescription(string desc) { _template.Description = desc; return this; }
        public TemplateBuilder WithVersion(string ver) { _template.Version = ver; return this; }

        public TemplateBuilder AddParameter(string name, object defaultValue = null, bool required = true, string desc = null)
        {
            _template.InputParameters[name] = new ParameterDefinition { Name = name, DefaultValue = defaultValue, IsRequired = required, Description = desc };
            return this;
        }

        public TemplateBuilder AddOutput(string externalName, string internalNodeKey, string internalOutputKey = null)
        {
            if (!_nodeManager.ContainsKey(internalNodeKey))
                throw new ArgumentException($"Internal node key '{internalNodeKey}' not found.");
            var node = _nodeManager.Get(internalNodeKey);
            var fullKey = string.IsNullOrEmpty(internalOutputKey) ? NodeKeyExtensions.GetDefaultOutputKey(node.Symbol, internalNodeKey) : internalOutputKey;
            _template.Outputs[externalName] = fullKey;
            return this;
        }

        public TemplateBuilder AddNode(string key, string symbol, Action<GraphNode> configure = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Node key cannot be null or empty.");
            var node = new GraphNode { Key = key, Symbol = symbol, Label = symbol };
            configure?.Invoke(node);
            _nodeManager.Add(node);
            _template.Nodes.Add(node);
            _template.OriginalKeys[node] = key;
            return this;
        }

        public ChainTemplate<GraphNode> Build()
        {
            if (!_template.IsValid(out var error)) throw new InvalidOperationException($"Template invalid: {error}");
            return _template;
        }
    }

    public static class GraphBuilderExtensions
    {
        public static string BuildToJson(this GraphBuilder builder, bool indented = true) =>
            JsonSerializer.Serialize(builder.Build(), new JsonSerializerOptions { WriteIndented = indented });
    }
}

#endregion



namespace GraphBuildingFramework
{
    using Graphkey.Core;
    using SkeletonFlow;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    #region GraphBuildContext

    public class GraphBuildContext
    {
        private readonly SkeletonContext _skeletonContext;
        private readonly List<object> _nodes;
        private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random());

        public GraphBuildContext(SkeletonContext skeletonContext)
        {
            _skeletonContext = skeletonContext ?? throw new ArgumentNullException(nameof(skeletonContext));

            if (_skeletonContext.Items.TryGetValue("Nodes", out var nodesObj))
            {
                if (nodesObj is List<object> existingNodes)
                    _nodes = existingNodes;
                else
                    throw new InvalidOperationException($"Context already has data with key 'Nodes' but of incompatible type '{nodesObj.GetType()}'. Expected 'List<object>'.");
            }
            else
            {
                _nodes = new List<object>();
                _skeletonContext.SetItem("Nodes", _nodes);
            }
        }

        public SkeletonContext SkeletonContext => _skeletonContext;
        public List<object> Nodes => _nodes;

        public string GenerateNumericId(int length = 10)
        {
            const string digits = "0123456789";
            var chars = new char[length];
            var rng = _random.Value;
            for (int i = 0; i < length; i++)
                chars[i] = digits[rng.Next(digits.Length)];
            return new string(chars);
        }

        public string GenerateGuidId() => Guid.NewGuid().ToString();

        public Placeholder GetPlaceholder(string key)
        {
            if (_skeletonContext.Items.TryGetValue(key, out var value) && value is Placeholder placeholder)
                return placeholder;
            throw new KeyNotFoundException($"Placeholder '{key}' not found in context.");
        }

        public Placeholder[] GetPlaceholders(IEnumerable<string> keys) =>
            keys.Select(GetPlaceholder).ToArray();

        public void SetPlaceholder(string key, Placeholder placeholder)
        {
            _skeletonContext.SetItem(key, placeholder);
        }

        public void SetPlaceholders(IEnumerable<string> keys, IEnumerable<Placeholder> placeholders)
        {
            var keyList = keys?.ToList() ?? throw new ArgumentNullException(nameof(keys));
            var placeholderList = placeholders?.ToList() ?? throw new ArgumentNullException(nameof(placeholders));

            if (keyList.Count != placeholderList.Count)
                throw new ArgumentException($"The number of keys ({keyList.Count}) must match the number of placeholders ({placeholderList.Count}).");

            for (int i = 0; i < keyList.Count; i++)
                SetPlaceholder(keyList[i], placeholderList[i]);
        }

        public Placeholder CreatePlaceholder(string id) => Placeholder.From(id)[0];

        public Placeholder CreatePlaceholder(string id, int outputIndex) => Placeholder.From(id, outputIndex);

        public Placeholder CreatePlaceholder(string id, string outputKey) => Placeholder.From(id, outputKey);
    }

    #endregion

    #region INodeBuilder

    public interface INodeBuilder
    {
        IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context);
    }

    #endregion

    #region Built-in Node Builders

    public static class GraphKeyNode
    {
        private const string DefaultNodeLabel = "TEST_1_Label";

        private static readonly System.Text.Json.JsonSerializerOptions _buildOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string GraphKeyNodeBuild(List<object> nodes)
        {
            var senodes = System.Text.Json.JsonSerializer.Serialize(nodes, _buildOptions);
            senodes = JsonKeyConverter.ConvertArrayValuesToQuotedString(
               senodes,
               new HashSet<string> { "Label", "ParameterKeys", "ResultKeys" },
               recursive: true);
            return senodes;
        }

        public static void GraphKeyNodeAddNode(
            List<object> nodes,
            string symbol,
            string nodeId,
            string funcName,
            object[] parameterKeys,
            object[] resultKeys)
        {
            dynamic node = new System.Dynamic.ExpandoObject();
            node.Symbol = symbol;
            node.Label = DefaultNodeLabel;
            node.NodeId = nodeId;

            dynamic config = new System.Dynamic.ExpandoObject();
            config.FuncName = funcName;
            config.ParameterKeys = parameterKeys;
            config.ResultKeys = resultKeys;
            node.FuncPipelineConfig = config;

            nodes.Add(node);
        }

        [Obsolete("Use the overload with explicit funcName parameter instead. This overload uses symbol as funcName.")]
        public static void GraphKeyNodeAddNode(
            List<object> nodes,
            string symbol,
            string nodeId,
            object[] parameterKeys,
            object[] resultKeys)
        {
            GraphKeyNodeAddNode(nodes, symbol, nodeId, symbol, parameterKeys, resultKeys);
        }
    }

    public class SingleNodeBuilder : INodeBuilder
    {
        private readonly string _nodeType;
        private readonly IEnumerable<string> _inputPlaceholderKeys;
        private readonly string _outputPlaceholderKey;
        private readonly string _funcName;
        private readonly Func<GraphBuildContext, string> _idGenerator;

        public SingleNodeBuilder(
            string nodeType,
            IEnumerable<string> inputPlaceholderKeys,
            string outputPlaceholderKey,
            string funcName = null,
            Func<GraphBuildContext, string> idGenerator = null)
        {
            _nodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
            _inputPlaceholderKeys = inputPlaceholderKeys ?? Enumerable.Empty<string>();
            _outputPlaceholderKey = outputPlaceholderKey ?? throw new ArgumentNullException(nameof(outputPlaceholderKey));
            _funcName = funcName;
            _idGenerator = idGenerator ?? (ctx => ctx.GenerateGuidId());
        }

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context)
        {
            var inputPlaceholders = context.GetPlaceholders(_inputPlaceholderKeys);
            for (int i = 0; i < inputPlaceholders.Length; i++)
            {
                if (inputPlaceholders[i] == null)
                    throw new InvalidOperationException($"Placeholder for key '{_inputPlaceholderKeys.ElementAt(i)}' was not found in the context.");
            }

            string nodeId = _idGenerator(context);

            AddGraphNode(context.Nodes, _nodeType, nodeId, _funcName, inputPlaceholders, new[] { _outputPlaceholderKey });

            var outputPlaceholder = context.CreatePlaceholder(nodeId);

            return new Dictionary<string, Placeholder> { { _outputPlaceholderKey, outputPlaceholder } };
        }

        private static void AddGraphNode(
            List<object> nodes,
            string symbol,
            string nodeId,
            string funcName,
            object[] parameterKeys,
            object[] resultKeys)
        {
            GraphKeyNode.GraphKeyNodeAddNode(nodes, symbol, nodeId, funcName ?? symbol, parameterKeys, resultKeys);
        }
    }

    public class SequentialNodeBuilder : INodeBuilder
    {
        private readonly IReadOnlyList<INodeBuilder> _builders;

        public SequentialNodeBuilder(params INodeBuilder[] builders)
        {
            _builders = builders?.ToList() ?? throw new ArgumentNullException(nameof(builders));
        }

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context)
        {
            var result = new Dictionary<string, Placeholder>();
            foreach (var builder in _builders)
            {
                var outputs = builder.Build(context);
                foreach (var kv in outputs)
                    result[kv.Key] = kv.Value;
            }
            return result;
        }
    }

    public class ConditionalNodeBuilder : INodeBuilder
    {
        private readonly Func<GraphBuildContext, bool> _condition;
        private readonly INodeBuilder _trueBuilder;
        private readonly INodeBuilder _falseBuilder;

        public ConditionalNodeBuilder(
            Func<GraphBuildContext, bool> condition,
            INodeBuilder trueBuilder,
            INodeBuilder falseBuilder = null)
        {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            _trueBuilder = trueBuilder ?? throw new ArgumentNullException(nameof(trueBuilder));
            _falseBuilder = falseBuilder;
        }

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context)
        {
            if (_condition(context))
                return _trueBuilder.Build(context);
            else if (_falseBuilder != null)
                return _falseBuilder.Build(context);
            else
                return new Dictionary<string, Placeholder>();
        }
    }

    #endregion

    #region IGraphComponent

    public interface IGraphComponent
    {
        string Name { get; }
        IReadOnlyList<string> InputKeys { get; }
        IReadOnlyList<string> OutputKeys { get; }

        IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context);
    }

    public class NodeBuilderComponent : IGraphComponent
    {
        private readonly string _name;
        private readonly IReadOnlyList<string> _inputKeys;
        private readonly IReadOnlyList<string> _outputKeys;
        private readonly INodeBuilder _builder;

        public NodeBuilderComponent(
            string name,
            IEnumerable<string> inputKeys,
            IEnumerable<string> outputKeys,
            INodeBuilder builder)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _inputKeys = inputKeys?.ToList() ?? new List<string>();
            _outputKeys = outputKeys?.ToList() ?? new List<string>();
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public string Name => _name;
        public IReadOnlyList<string> InputKeys => _inputKeys;
        public IReadOnlyList<string> OutputKeys => _outputKeys;

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context) =>
            _builder.Build(context);
    }

    public class CompositeGraphComponent : IGraphComponent
    {
        private readonly string _name;
        private readonly IReadOnlyList<IGraphComponent> _children;
        private readonly IReadOnlyDictionary<string, string> _inputMappings;
        private readonly IReadOnlyDictionary<string, string> _outputMappings;

        public CompositeGraphComponent(
            string name,
            IEnumerable<IGraphComponent> children,
            IReadOnlyDictionary<string, string> inputMappings = null,
            IReadOnlyDictionary<string, string> outputMappings = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _children = children?.ToList() ?? new List<IGraphComponent>();
            _inputMappings = inputMappings ?? new Dictionary<string, string>();
            _outputMappings = outputMappings ?? new Dictionary<string, string>();

            var seenInternalKeys = new HashSet<string>();
            foreach (var kv in _inputMappings)
            {
                if (!seenInternalKeys.Add(kv.Value))
                    throw new ArgumentException($"CompositeGraphComponent '{name}': input mapping has duplicate internal key '{kv.Value}'. Multiple external keys cannot map to the same internal key.");
            }
        }

        public string Name => _name;

        public IReadOnlyList<string> InputKeys => _inputMappings.Keys.ToList();

        public IReadOnlyList<string> OutputKeys => _outputMappings.Keys.ToList();

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context)
        {
            var finalOutputs = new Dictionary<string, Placeholder>();

            foreach (var child in _children)
            {
                var childInputKeys = child.InputKeys;
                foreach (var map in _inputMappings)
                {
                    if (childInputKeys.Contains(map.Value))
                    {
                        var externalPlaceholder = context.GetPlaceholder(map.Key);
                        context.SetPlaceholder(map.Value, externalPlaceholder);
                    }
                }

                var childOutputs = child.Build(context);

                foreach (var kv in childOutputs)
                {
                    context.SetPlaceholder(kv.Key, kv.Value);
                }

                foreach (var outMap in _outputMappings)
                {
                    if (childOutputs.TryGetValue(outMap.Value, out var placeholder))
                        finalOutputs[outMap.Key] = placeholder;
                }
            }

            return finalOutputs;
        }
    }

    #endregion

    #region Integration with SkeletonFlow

    public class GraphComponentSkeleton : SkeletonBase<object, object>
    {
        private readonly IGraphComponent _component;

        public GraphComponentSkeleton(IGraphComponent component)
            : base($"GraphComponent_{component.Name}")
        {
            _component = component ?? throw new ArgumentNullException(nameof(component));
        }

        protected override object ExecuteCore(object input, SkeletonContext ctx)
        {
            var buildContext = new GraphBuildContext(ctx);

            foreach (var key in _component.InputKeys)
            {
                _ = buildContext.GetPlaceholder(key);
            }

            var outputs = _component.Build(buildContext);

            foreach (var kv in outputs)
                buildContext.SetPlaceholder(kv.Key, kv.Value);

            return input;
        }

        protected override Task<object> ExecuteCoreAsync(object input, SkeletonContext ctx)
            => Task.FromResult(ExecuteCore(input, ctx));
    }

    #endregion
}

namespace GraphBuildingFramework.Advanced
{
    using GraphBuildingFramework;
    using Graphkey.Core;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class MultiNodeBuilder : INodeBuilder
    {
        private readonly string _nodeType;
        private readonly IEnumerable<string> _inputKeys;
        private readonly Func<int, string> _outputKeyFormatter;
        private readonly Func<GraphBuildContext, string> _idGenerator;

        public MultiNodeBuilder(
            string nodeType,
            IEnumerable<string> inputKeys,
            Func<int, string> outputKeyFormatter = null,
            Func<GraphBuildContext, string> idGenerator = null)
        {
            _nodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
            _inputKeys = inputKeys ?? throw new ArgumentNullException(nameof(inputKeys));
            _outputKeyFormatter = outputKeyFormatter ?? (i => $"{nodeType}_Output_{i}");
            _idGenerator = idGenerator ?? (ctx => ctx.GenerateGuidId());
        }

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context)
        {
            var result = new Dictionary<string, Placeholder>();
            var inputs = _inputKeys.ToList();

            for (int i = 0; i < inputs.Count; i++)
            {
                var nodeId = _idGenerator(context);
                var inputPlaceholder = context.GetPlaceholder(inputs[i]);
                var outputKey = _outputKeyFormatter(i + 1);

                GraphKeyNode.GraphKeyNodeAddNode(
                    context.Nodes,
                    _nodeType,
                    nodeId,
                    _nodeType,
                    new object[] { inputPlaceholder },
                    new[] { outputKey }
                );

                var placeholder = context.CreatePlaceholder(nodeId);
                result[outputKey] = placeholder;
            }

            return result;
        }
    }

    public class ParameterBuilder
    {
        private readonly List<object> _parameters = new List<object>();

        public ParameterBuilder AddConstant(object value)
        {
            _parameters.Add(value);
            return this;
        }

        public ParameterBuilder AddPlaceholder(Placeholder placeholder)
        {
            _parameters.Add(placeholder ?? throw new ArgumentNullException(nameof(placeholder)));
            return this;
        }

        public ParameterBuilder AddPlaceholderFromContext(GraphBuildContext context, string key)
        {
            _parameters.Add(context.GetPlaceholder(key));
            return this;
        }

        public ParameterBuilder AddRange(IEnumerable<object> values)
        {
            _parameters.AddRange(values);
            return this;
        }

        public object[] Build() => _parameters.ToArray();
    }

    public static class NodeBuilderExtensions
    {
        public static IReadOnlyDictionary<string, Placeholder> ExecuteBuilder(
            this GraphBuildContext context,
            INodeBuilder builder)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Build(context);
        }

        public static GraphBuildContext StoreOutputs(
            this GraphBuildContext context,
            IReadOnlyDictionary<string, Placeholder> outputs)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (outputs == null) throw new ArgumentNullException(nameof(outputs));

            foreach (var kv in outputs)
                context.SetPlaceholder(kv.Key, kv.Value);

            return context;
        }

        public static GraphBuildContext ExecuteAndStore(
            this GraphBuildContext context,
            INodeBuilder builder)
        {
            var outputs = context.ExecuteBuilder(builder);
            return context.StoreOutputs(outputs);
        }
    }

    public class CustomParameterSingleNodeBuilder : INodeBuilder
    {
        private readonly string _nodeType;
        private readonly string _outputPlaceholderKey;
        private readonly string _funcName;
        private readonly Func<GraphBuildContext, string> _idGenerator;
        private readonly Func<GraphBuildContext, object[]> _parameterBuilder;

        public CustomParameterSingleNodeBuilder(
            string nodeType,
            string outputPlaceholderKey,
            string funcName = null,
            Func<GraphBuildContext, string> idGenerator = null,
            Func<GraphBuildContext, object[]> parameterBuilder = null)
        {
            _nodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
            _outputPlaceholderKey = outputPlaceholderKey ?? throw new ArgumentNullException(nameof(outputPlaceholderKey));
            _funcName = funcName;
            _idGenerator = idGenerator ?? (ctx => ctx.GenerateGuidId());
            _parameterBuilder = parameterBuilder ?? (ctx => Array.Empty<object>());
        }

        public IReadOnlyDictionary<string, Placeholder> Build(GraphBuildContext context)
        {
            var nodeId = _idGenerator(context);
            var parameters = _parameterBuilder(context);
            var resultKeys = new[] { _outputPlaceholderKey };

            GraphKeyNode.GraphKeyNodeAddNode(
                context.Nodes,
                _nodeType,
                nodeId,
                _funcName ?? _nodeType,
                parameters,
                resultKeys
            );

            var placeholder = context.CreatePlaceholder(nodeId);
            return new Dictionary<string, Placeholder> { { _outputPlaceholderKey, placeholder } };
        }
    }
}


#endregion

