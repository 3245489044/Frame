
#nullable enable

using System.Collections.Generic;
using GraphFoundation.Abstractions;
using GraphFramework.Models;

namespace GraphFramework.Directives
{
    public interface IDirectiveApplier<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        DirectiveResult Apply(
            IGraph<TKey, TNode, TEdge> graph,
            GraphUpdateDirective<TKey, TNode, TEdge> directive);

        List<DirectiveResult> ApplyBatch(
            IGraph<TKey, TNode, TEdge> graph,
            IEnumerable<GraphUpdateDirective<TKey, TNode, TEdge>> directives);

        DirectiveValidationResult Validate(
            IGraph<TKey, TNode, TEdge> graph,
            GraphUpdateDirective<TKey, TNode, TEdge> directive);
    }

    public class DefaultDirectiveApplier<TKey, TNode, TEdge> : IDirectiveApplier<TKey, TNode, TEdge>
        where TKey : notnull
        where TNode : class
        where TEdge : class
    {
        protected readonly Func<string, string> KeyFactory;

        protected readonly Func<NodeTemplate<TKey, TNode>, TNode> NodeDataFactory;

        public DefaultDirectiveApplier(
            Func<string, string>? keyFactory = null,
            Func<NodeTemplate<TKey, TNode>, TNode>? nodeDataFactory = null)
        {
            KeyFactory = keyFactory ?? (k => k);
            NodeDataFactory = nodeDataFactory ?? (t => (TNode)(object)t.NodeData!);
        }

        public DirectiveResult Apply(
            IGraph<TKey, TNode, TEdge> graph,
            GraphUpdateDirective<TKey, TNode, TEdge> directive)
        {
            var result = new DirectiveResult { DirectiveId = directive.Id };

            try
            {
                switch (directive)
                {
                    case InsertNodeDirective<TKey, TNode, TEdge> insert:
                        ApplyInsert(graph, insert, result);
                        break;
                    case InsertChainDirective<TKey, TNode, TEdge> insertChain:
                        ApplyInsertChain(graph, insertChain, result);
                        break;
                    case DeleteNodeDirective<TKey, TNode, TEdge> delete:
                        ApplyDelete(graph, delete, result);
                        break;
                    case DeleteChainDirective<TKey, TNode, TEdge> deleteChain:
                        ApplyDeleteChain(graph, deleteChain, result);
                        break;
                    case ReplaceNodeDirective<TKey, TNode, TEdge> replace:
                        ApplyReplace(graph, replace, result);
                        break;
                    case MergeChainDirective<TKey, TNode, TEdge> merge:
                        ApplyMergeChain(graph, merge, result);
                        break;
                    case MergeNodesDirective<TKey, TNode, TEdge> mergeNodes:
                        ApplyMergeNodes(graph, mergeNodes, result);
                        break;
                    case RedirectEdgeDirective<TKey, TNode, TEdge> redirect:
                        ApplyRedirect(graph, redirect, result);
                        break;
                    case CompositeDirective<TKey, TNode, TEdge> composite:
                        ApplyComposite(graph, composite, result);
                        break;
                    default:
                        result.Success = false;
                        result.ErrorMessage = $"不支持的指令类型: {directive.GetType().Name}";
                        break;
                }
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public List<DirectiveResult> ApplyBatch(
            IGraph<TKey, TNode, TEdge> graph,
            IEnumerable<GraphUpdateDirective<TKey, TNode, TEdge>> directives)
        {
            var results = new List<DirectiveResult>();
            foreach (var directive in directives)
            {
                results.Add(Apply(graph, directive));
            }
            return results;
        }

        public DirectiveValidationResult Validate(
            IGraph<TKey, TNode, TEdge> graph,
            GraphUpdateDirective<TKey, TNode, TEdge> directive)
        {
            var result = new DirectiveValidationResult { IsValid = true };

            switch (directive)
            {
                case InsertNodeDirective<TKey, TNode, TEdge> insert:
                    ValidateInsert(graph, insert, result);
                    break;
                case DeleteNodeDirective<TKey, TNode, TEdge> delete:
                    if (!graph.ContainsNode(delete.NodeKey))
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"节点不存在: {delete.NodeKey}";
                    }
                    break;
                case ReplaceNodeDirective<TKey, TNode, TEdge> replace:
                    if (!graph.ContainsNode(replace.OldNodeKey))
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"旧节点不存在: {replace.OldNodeKey}";
                    }
                    break;
                case RedirectEdgeDirective<TKey, TNode, TEdge> redirect:
                    if (!graph.ContainsNode(redirect.FromNodeKey))
                        result.Warnings.Add($"源节点不存在: {redirect.FromNodeKey}");
                    if (!graph.ContainsNode(redirect.NewTargetKey))
                        result.Warnings.Add($"新目标节点不存在: {redirect.NewTargetKey}");
                    break;
            }

            return result;
        }


        private void ApplyInsert(IGraph<TKey, TNode, TEdge> graph,
            InsertNodeDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            foreach (var template in directive.NodesToInsert)
            {
                var nodeKey = template.GenerateKey(s => (TKey)(object)s);
                var nodeData = NodeDataFactory(template);
                graph.AddNode(nodeKey, nodeData);
            }
            result.Success = true;
            result.ResultData = new { InsertedCount = directive.NodesToInsert.Count };
        }

        private void ApplyInsertChain(IGraph<TKey, TNode, TEdge> graph,
            InsertChainDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            var insertedKeys = new List<TKey>();
            TKey? prevKey = default;

            foreach (var template in directive.NodesToInsert)
            {
                var nodeKey = template.GenerateKey(s => (TKey)(object)s);
                var nodeData = NodeDataFactory(template);
                graph.AddNode(nodeKey, nodeData);

                if (prevKey != null)
                {
                    var edgeId = KeyFactory($"edge_{prevKey}_{nodeKey}");
                    graph.AddEdge(edgeId, prevKey, nodeKey, true, default, 1.0);
                }

                insertedKeys.Add(nodeKey);
                prevKey = nodeKey;
            }

            result.Success = true;
            result.ResultData = new { InsertedCount = insertedKeys.Count, Keys = insertedKeys };
        }

        private void ApplyDelete(IGraph<TKey, TNode, TEdge> graph,
            DeleteNodeDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            if (graph.ContainsNode(directive.NodeKey))
            {
                graph.RemoveNode(directive.NodeKey);
                result.Success = true;
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = $"节点不存在: {directive.NodeKey}";
            }
        }

        private void ApplyDeleteChain(IGraph<TKey, TNode, TEdge> graph,
            DeleteChainDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            foreach (var nodeKey in directive.DeletedNodeKeys)
            {
                if (graph.ContainsNode(nodeKey))
                    graph.RemoveNode(nodeKey);
            }
            result.Success = true;
        }

        private void ApplyReplace(IGraph<TKey, TNode, TEdge> graph,
            ReplaceNodeDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            if (!graph.ContainsNode(directive.OldNodeKey))
            {
                result.Success = false;
                result.ErrorMessage = $"旧节点不存在: {directive.OldNodeKey}";
                return;
            }

            var newNodeKey = directive.NewNode.GenerateKey(s => (TKey)(object)s);
            var newNodeData = NodeDataFactory(directive.NewNode);

            var neighbors = graph.GetNeighbors(directive.OldNodeKey).ToList();

            graph.AddNode(newNodeKey, newNodeData);

            if (directive.PreserveConnections)
            {
                foreach (var neighbor in neighbors)
                {
                    var edgeId = KeyFactory($"edge_{newNodeKey}_{neighbor}");
                    graph.AddEdge(edgeId, newNodeKey, neighbor, true, default, 1.0);
                }
            }

            graph.RemoveNode(directive.OldNodeKey);

            result.Success = true;
            result.ResultData = new { OldKey = directive.OldNodeKey, NewKey = newNodeKey };
        }

        private void ApplyMergeChain(IGraph<TKey, TNode, TEdge> graph,
            MergeChainDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            result.Success = true;
            result.ResultData = new { Source = directive.SourceChainId, Target = directive.TargetChainId };
        }

        private void ApplyMergeNodes(IGraph<TKey, TNode, TEdge> graph,
            MergeNodesDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            result.Success = true;
        }

        private void ApplyRedirect(IGraph<TKey, TNode, TEdge> graph,
            RedirectEdgeDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            if (!graph.ContainsNode(directive.FromNodeKey))
            {
                result.Success = false;
                result.ErrorMessage = $"源节点不存在: {directive.FromNodeKey}";
                return;
            }
            if (!graph.ContainsNode(directive.NewTargetKey))
            {
                result.Success = false;
                result.ErrorMessage = $"新目标节点不存在: {directive.NewTargetKey}";
                return;
            }

            if (directive.RemoveOldEdge && directive.OldTargetKey != null)
            {
                var oldEdgeIds = graph.GetAllEdgeIds()
                    .Where(edgeId =>
                    {
                        var source = graph.GetEdgeSource(edgeId);
                        var target = graph.GetEdgeTarget(edgeId);
                        return EqualityComparer<TKey>.Default.Equals(source, directive.FromNodeKey)
                            && EqualityComparer<TKey>.Default.Equals(target, directive.OldTargetKey);
                    })
                    .ToList();
                foreach (var edgeId in oldEdgeIds)
                {
                    graph.RemoveEdge(edgeId);
                }
            }

            var newEdgeId = KeyFactory($"edge_{directive.FromNodeKey}_{directive.NewTargetKey}");
            graph.AddEdge(newEdgeId, directive.FromNodeKey, directive.NewTargetKey, true, default, 1.0);

            result.Success = true;
            result.ResultData = new { From = directive.FromNodeKey, NewTarget = directive.NewTargetKey };
        }

        private void ApplyComposite(IGraph<TKey, TNode, TEdge> graph,
            CompositeDirective<TKey, TNode, TEdge> directive, DirectiveResult result)
        {
            var results = new List<DirectiveResult>();
            foreach (var subDirective in directive.Directives)
            {
                var subResult = Apply(graph, subDirective);
                results.Add(subResult);
                if (!subResult.Success && directive.StopOnError)
                    break;
            }
            result.Success = results.TrueForAll(r => r.Success);
            result.ResultData = results;
        }

        private void ValidateInsert(IGraph<TKey, TNode, TEdge> graph,
            InsertNodeDirective<TKey, TNode, TEdge> directive, DirectiveValidationResult result)
        {
            if (directive.NodesToInsert.Count == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "没有要插入的节点";
            }
        }
    }
}
