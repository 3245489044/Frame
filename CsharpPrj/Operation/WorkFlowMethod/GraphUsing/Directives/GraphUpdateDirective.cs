
#region USINGS
using WorkFlowMethod.Skeletons.NodeProcess;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Directives
{
    using Models = global::WorkFlowMethod.Skeletons.NodeProcess.Models;




    public enum GraphUpdateType
    {
        Insert,
        Delete,
        Replace,
        Merge,
        Split,
        Redirect,
        Composite
    }

    public enum EdgeDirection
    {
        AfterSource,
        BeforeTarget,
        Between
    }

    public enum ChainMergeStrategy
    {
        Append,
        Prepend,
        Interleave
    }

    public enum DirectiveInsertPosition
    {
        AfterSource,
        BeforeTarget,
        Between,
        AtStart,
        AtEnd
    }

    public enum MergeNodesStrategy
    {
        CombineOutputs,
        PreserveFirst,
        PreserveLast
    }

    public enum CompositeStrategy
    {
        Sequential,
        Parallel
    }

    public enum SymbolMatchMode
    {
        First,
        Last,
        All,
        Nearest
    }


    public abstract class GraphUpdateDirective
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string? WorkflowId { get; set; }
        public abstract GraphUpdateType UpdateType { get; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }


    public class InsertNodeDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Insert;
        public string SourceNodeKey { get; set; } = string.Empty;
        public string TargetNodeKey { get; set; } = string.Empty;
        public EdgeDirection Direction { get; set; } = EdgeDirection.AfterSource;
        public DirectiveInsertPosition Position { get; set; } = DirectiveInsertPosition.AfterSource;
        public int? InsertAfterSequence { get; set; }
        public int? InsertBeforeSequence { get; set; }
        public string InsertAfterSymbol { get; set; } = string.Empty;
        public string InsertBeforeSymbol { get; set; } = string.Empty;
        public SymbolMatchMode SymbolMatchMode { get; set; } = SymbolMatchMode.First;
        public List<NodeTemplate> NodesToInsert { get; set; } = new();
        public List<EdgeTemplate>? EdgesToCreate { get; set; }
    }

    public class InsertChainDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Insert;
        public int? InsertAfterSequence { get; set; }
        public int? InsertBeforeSequence { get; set; }
        public string InsertAfterNodeCallkey { get; set; } = string.Empty;
        public string InsertBeforeNodeCallkey { get; set; } = string.Empty;
        public string InsertAfterSymbol { get; set; } = string.Empty;
        public string InsertBeforeSymbol { get; set; } = string.Empty;
        public SymbolMatchMode SymbolMatchMode { get; set; } = SymbolMatchMode.First;
        public List<string> ChainJsonConfigs { get; init; } = new();
        public List<NodeTemplate> ParsedNodes { get; set; } = new();
        public int ListCount { get; set; } = 1;
    }

    public class DeleteNodeDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Delete;
        public string NodeKey { get; set; } = string.Empty;
        public int? TargetSequence { get; set; }
        public bool ReconnectNeighbors { get; init; } = true;
        public bool DeleteOrphanedChains { get; init; } = true;
    }

    public class DeleteChainDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Delete;
        public string ChainId { get; init; } = string.Empty;
        public bool Cascade { get; init; } = false;
        public List<string> DeletedNodeKeys { get; set; } = new();
    }

    public class ReplaceNodeDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Replace;
        public string OldNodeKey { get; init; } = string.Empty;
        public NodeTemplate NewNode { get; init; } = null!;
        public bool PreserveConnections { get; init; } = true;
        public bool PreserveSymbol { get; init; } = true;
    }

    public class MergeChainDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Merge;
        public string SourceChainId { get; init; } = string.Empty;
        public string TargetChainId { get; init; } = string.Empty;
        public ChainMergeStrategy Strategy { get; init; } = ChainMergeStrategy.Append;
    }

    public class MergeNodesDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Merge;
        public List<string> NodeKeysToMerge { get; init; } = new();
        public NodeTemplate MergedNode { get; init; } = null!;
        public MergeNodesStrategy Strategy { get; init; } = MergeNodesStrategy.CombineOutputs;
    }

    public class RedirectEdgeDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Redirect;
        public string FromNodeKey { get; init; } = string.Empty;
        public string OldTargetKey { get; init; } = string.Empty;
        public string NewTargetKey { get; init; } = string.Empty;
        public bool RemoveOldEdge { get; init; } = true;
    }


    public class NodeTemplate
    {
        public string? Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string? Callkey { get; set; }
        public object? BaseConfig { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();

        public object ToNodeConfig(object? keyGenerator = null)
        {
            if (BaseConfig != null) return BaseConfig;
            
            var config = new NodeConfig
            {
                Symbol = Symbol,
                Label = Label ?? Symbol,
                basicConfig = new NodeConfig.BasicConfig
                {
                    Callkey = Callkey ?? GenerateKey(keyGenerator)
                },
                funcPipelineConfig = new NodeConfig.FuncPipelineConfig
                {
                    FuncName = Symbol
                }
            };
            
            NodeConfigFactory.CompleteConfig(config);
            return config;
        }


        public string GenerateKey(object? keyGenerator = null) => Callkey ?? Id ?? Guid.NewGuid().ToString("N");
    }

    public class EdgeTemplate
    {
        public string? EdgeId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public object? Data { get; set; }
        public double Weight { get; set; } = 1.0;

        public string GenerateEdgeId() => EdgeId ?? $"edge_{Source}_{Target}_{Guid.NewGuid().ToString("N")}";
    }

    public class CompositeDirective : GraphUpdateDirective
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Composite;
        public List<GraphUpdateDirective> Directives { get; set; } = new();
        public CompositeStrategy Strategy { get; set; } = CompositeStrategy.Sequential;
        public bool StopOnError { get; set; } = true;
        public List<DirectiveResult> Results { get; set; } = new();
    }

    public class DirectiveResult
    {
        public string DirectiveId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public object? ResultData { get; set; }
    }

    public class DirectiveValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Warnings { get; set; } = new();
    }


    public class DirectiveValidator
    {
        public static DirectiveValidationResult Validate(GraphUpdateDirective directive) =>
            directive switch
            {
                InsertNodeDirective insert => ValidateInsert(insert),
                InsertChainDirective insertChain => ValidateInsertChain(insertChain),
                DeleteNodeDirective delete => ValidateDelete(delete),
                ReplaceNodeDirective replace => ValidateReplace(replace),
                MergeChainDirective merge => ValidateMerge(merge),
                RedirectEdgeDirective redirect => ValidateRedirect(redirect),
                CompositeDirective composite => ValidateComposite(composite),
                _ => new DirectiveValidationResult { IsValid = false, ErrorMessage = "Unknown directive type" }
            };

        private static DirectiveValidationResult ValidateInsert(InsertNodeDirective directive) =>
            directive.NodesToInsert == null || directive.NodesToInsert.Count == 0
                ? new DirectiveValidationResult { IsValid = false, ErrorMessage = "No nodes to insert" }
                : new DirectiveValidationResult { IsValid = true };

        private static DirectiveValidationResult ValidateInsertChain(InsertChainDirective directive) =>
            directive.ChainJsonConfigs == null || directive.ChainJsonConfigs.Count == 0
                ? new DirectiveValidationResult { IsValid = false, ErrorMessage = "No chain configs" }
                : new DirectiveValidationResult { IsValid = true };

        private static DirectiveValidationResult ValidateDelete(DeleteNodeDirective directive) =>
            string.IsNullOrEmpty(directive.NodeKey)
                ? new DirectiveValidationResult { IsValid = false, ErrorMessage = "Node key is required" }
                : new DirectiveValidationResult { IsValid = true };

        private static DirectiveValidationResult ValidateReplace(ReplaceNodeDirective directive) =>
            string.IsNullOrEmpty(directive.OldNodeKey) || directive.NewNode == null
                ? new DirectiveValidationResult { IsValid = false, ErrorMessage = "Old node key and new node are required" }
                : new DirectiveValidationResult { IsValid = true };

        private static DirectiveValidationResult ValidateMerge(MergeChainDirective directive) =>
            string.IsNullOrEmpty(directive.SourceChainId) || string.IsNullOrEmpty(directive.TargetChainId)
                ? new DirectiveValidationResult { IsValid = false, ErrorMessage = "Source and target chain IDs are required" }
                : new DirectiveValidationResult { IsValid = true };

        private static DirectiveValidationResult ValidateRedirect(RedirectEdgeDirective directive) =>
            string.IsNullOrEmpty(directive.FromNodeKey) || string.IsNullOrEmpty(directive.OldTargetKey) || string.IsNullOrEmpty(directive.NewTargetKey)
                ? new DirectiveValidationResult { IsValid = false, ErrorMessage = "From, old target, and new target keys are required" }
                : new DirectiveValidationResult { IsValid = true };

        private static DirectiveValidationResult ValidateComposite(CompositeDirective directive)
        {
            if (directive.Directives == null || directive.Directives.Count == 0)
                return new DirectiveValidationResult { IsValid = false, ErrorMessage = "No directives in composite" };

            var warnings = new List<string>();
            foreach (var subDirective in directive.Directives)
            {
                var result = Validate(subDirective);
                if (!result.IsValid)
                    return new DirectiveValidationResult { IsValid = false, ErrorMessage = $"Invalid sub-directive: {result.ErrorMessage}" };
                warnings.AddRange(result.Warnings);
            }

            return new DirectiveValidationResult { IsValid = true, Warnings = warnings };
        }
    }
}



