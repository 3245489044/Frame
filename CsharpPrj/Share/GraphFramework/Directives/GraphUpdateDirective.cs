
#nullable enable

using System;
using System.Collections.Generic;
using GraphFramework.Models;

namespace GraphFramework.Directives
{
    public abstract class GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");

        public TKey? WorkflowId { get; set; }

        public abstract GraphUpdateType UpdateType { get; }

        public Dictionary<string, object?> Metadata { get; set; } = new();
    }


    public class InsertNodeDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Insert;

        public TKey? SourceNodeKey { get; set; }

        public TKey? TargetNodeKey { get; set; }

        public EdgeDirection Direction { get; set; } = EdgeDirection.AfterSource;

        public DirectiveInsertPosition Position { get; set; } = DirectiveInsertPosition.AfterSource;

        public List<NodeTemplate<TKey, TNode>> NodesToInsert { get; set; } = new();

        public List<EdgeTemplate<TKey>>? EdgesToCreate { get; set; }
    }

    public class InsertChainDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Insert;

        public TKey? InsertAfterNodeKey { get; set; }

        public TKey? InsertBeforeNodeKey { get; set; }

        public List<NodeTemplate<TKey, TNode>> NodesToInsert { get; set; } = new();

        public int ListCount { get; set; } = 1;
    }


    public class DeleteNodeDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Delete;

        public TKey NodeKey { get; set; } = default!;

        public bool ReconnectNeighbors { get; init; } = true;

        public bool DeleteOrphanedChains { get; init; } = true;
    }

    public class DeleteChainDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Delete;

        public TKey ChainId { get; init; } = default!;

        public bool Cascade { get; init; } = false;

        public List<TKey> DeletedNodeKeys { get; set; } = new();
    }


    public class ReplaceNodeDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Replace;

        public TKey OldNodeKey { get; init; } = default!;

        public NodeTemplate<TKey, TNode> NewNode { get; init; } = null!;

        public bool PreserveConnections { get; init; } = true;

        public bool PreserveSymbol { get; init; } = true;
    }


    public class MergeChainDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Merge;

        public TKey SourceChainId { get; init; } = default!;

        public TKey TargetChainId { get; init; } = default!;

        public ChainMergeStrategy Strategy { get; init; } = ChainMergeStrategy.Append;
    }

    public class MergeNodesDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Merge;

        public List<TKey> NodeKeysToMerge { get; init; } = new();

        public NodeTemplate<TKey, TNode> MergedNode { get; init; } = null!;

        public MergeNodesStrategy Strategy { get; init; } = MergeNodesStrategy.CombineOutputs;
    }


    public class RedirectEdgeDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Redirect;

        public TKey FromNodeKey { get; init; } = default!;

        public TKey OldTargetKey { get; init; } = default!;

        public TKey NewTargetKey { get; init; } = default!;

        public bool RemoveOldEdge { get; init; } = true;
    }


    public class CompositeDirective<TKey, TNode, TEdge> : GraphUpdateDirective<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        public override GraphUpdateType UpdateType => GraphUpdateType.Composite;

        public List<GraphUpdateDirective<TKey, TNode, TEdge>> Directives { get; set; } = new();

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
}
