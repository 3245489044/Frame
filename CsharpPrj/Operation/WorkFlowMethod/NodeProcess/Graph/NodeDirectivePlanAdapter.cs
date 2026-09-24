
#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using GraphFramework.Directives;
using GraphFramework.Models;
using Modules.Node.Diagnostics;
using Modules.Node.Spec;
#endregion

namespace WorkFlowMethod.Skeletons.NodeProcess.Graph
{
    /// <summary>
    /// 改图指令 → 框架 <see cref="NodeDirectiveSet"/> 的适配（A-2.7，U8 = 独立声明段）：
    /// 版本单调 + <b>只产计划</b>（不直写图/库）——计划可审计重放；版本回退被拒并可见化。
    /// 种类归一唯一来源 = 框架 <see cref="DirectiveKind"/>（宿主类型只做投影，不另立语义）。
    /// </summary>
    public static class NodeDirectivePlanAdapter
    {
        /// <summary>指令声明中的版本键（缺省 = 计划版本）。</summary>
        public const string VersionMetadataKey = "version";

        /// <summary>产改图计划：<paramref name="version"/> = 计划版本（快照版本 + 1 前的声明版本）。</summary>
        public static DirectivePlan Plan<TKey, TNode, TEdge>(
            int version,
            IEnumerable<GraphUpdateDirective<TKey, TNode, TEdge>>? directives,
            INodeDiagnosticsSink? diagnostics = null)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var set = new NodeDirectiveSet(diagnostics);

            foreach (var directive in directives ?? Enumerable.Empty<GraphUpdateDirective<TKey, TNode, TEdge>>())
            {
                set.Add(ToNodeDirective(version, directive));
            }

            var fromVersion = Math.Max(0, Math.Min(version - 1, set.CurrentVersion));
            return set.Plan(fromVersion);
        }

        public static NodeDirective ToNodeDirective<TKey, TNode, TEdge>(
            int version,
            GraphUpdateDirective<TKey, TNode, TEdge> directive)
            where TKey : notnull
            where TNode : class
            where TEdge : class
        {
            var declaredVersion = directive.Metadata.TryGetValue(VersionMetadataKey, out var raw) && raw is int declared
                ? declared
                : version;

            return new NodeDirective(
                declaredVersion,
                MapKind(directive.UpdateType),
                TargetOf(directive),
                MatchPattern: null,
                PayloadJson: null);
        }

        /// <summary>改图类型 → 声明种类（唯一归一；新增类型须在此声明映射）。</summary>
        public static DirectiveKind MapKind(GraphUpdateType updateType) => updateType switch
        {
            GraphUpdateType.Insert => DirectiveKind.AddNode,
            GraphUpdateType.Delete => DirectiveKind.RemoveNode,
            GraphUpdateType.Replace => DirectiveKind.ReplaceNode,
            GraphUpdateType.Merge => DirectiveKind.MergeNodes,
            GraphUpdateType.Split => DirectiveKind.SplitNode,
            GraphUpdateType.Redirect => DirectiveKind.RedirectEdge,
            GraphUpdateType.Composite => DirectiveKind.Composite,
            _ => throw new ArgumentOutOfRangeException(nameof(updateType), updateType, "未声明的改图类型"),
        };

        private static string TargetOf<TKey, TNode, TEdge>(GraphUpdateDirective<TKey, TNode, TEdge> directive)
            where TKey : notnull
            where TNode : class
            where TEdge : class
            => directive switch
            {
                InsertNodeDirective<TKey, TNode, TEdge> insert => $"{insert.SourceNodeKey}->{insert.TargetNodeKey}",
                InsertChainDirective<TKey, TNode, TEdge> insertChain => $"{insertChain.InsertAfterNodeKey}|{insertChain.InsertBeforeNodeKey}",
                DeleteNodeDirective<TKey, TNode, TEdge> delete => $"{delete.NodeKey}",
                DeleteChainDirective<TKey, TNode, TEdge> deleteChain => $"{deleteChain.ChainId}",
                ReplaceNodeDirective<TKey, TNode, TEdge> replace => $"{replace.OldNodeKey}",
                MergeChainDirective<TKey, TNode, TEdge> mergeChain => $"{mergeChain.SourceChainId}|{mergeChain.TargetChainId}",
                MergeNodesDirective<TKey, TNode, TEdge> mergeNodes => string.Join(",", mergeNodes.NodeKeysToMerge),
                RedirectEdgeDirective<TKey, TNode, TEdge> redirect => $"{redirect.FromNodeKey}->{redirect.NewTargetKey}",
                CompositeDirective<TKey, TNode, TEdge> composite => $"composite:{composite.Directives.Count}",
                _ => directive.Id,
            };
    }
}
