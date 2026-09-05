#region USINGS
using System;
using System.Collections.Generic;
using System.Linq;
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.DefaultImplementations;
using Share.LogicComposition.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Deduplication
{
    /// <summary>
    /// 节点链合并 + 去重门面 —— 复用 LogicComposition 的复杂链合并（<see cref="DefaultChainComposer{TElement}"/>）：
    /// 把多段节点链（<see cref="Chain{TKey,TValue}"/> / 节点序列）按 <see cref="NodeConfig.basicConfig.Callkey"/> 合并为一段，
    /// 由 <see cref="DuplicateHandling"/> 决定去重语义（Skip 保留首现 / Overwrite 后者覆盖 / Merge 深合并）。
    /// Agent 侧（DynamicWorkflowMerger）以此替代扁平拼接，接入 WorkFlowMethod 既有的复杂去重与链合并逻辑。
    /// </summary>
    public static class NodeChainMergeFacade
    {
        private static readonly IChainComposer<NodeConfig> Composer =
            new DefaultChainComposer<NodeConfig>(
                validator: new DefaultCompositionValidator(),
                keyExtractor: n => n.basicConfig?.Callkey,
                mergeStrategy: new DefaultMergeStrategy<NodeConfig>());

        /// <summary>链合并 + 去重：多段链 → 一段（返回完整合并结果，含冲突/统计）。</summary>
        public static MergeResult<IEnumerable<NodeConfig>> Merge(
            IEnumerable<IEnumerable<NodeConfig>> chains,
            DuplicateHandling handling = DuplicateHandling.Skip)
        {
            if (chains == null) throw new ArgumentNullException(nameof(chains));
            return Composer.Compose(chains, new CompositionOptions
            {
                DuplicateHandling = handling,
                ValidateBeforeMerge = true,
                TrackConflicts = true,
            });
        }

        /// <summary>便捷：返回去重合并后的节点列表（失败/空 → 空列表）。</summary>
        public static IReadOnlyList<NodeConfig> MergeNodes(
            IEnumerable<IEnumerable<NodeConfig>> chains,
            DuplicateHandling handling = DuplicateHandling.Skip)
        {
            var result = Merge(chains, handling);
            return result.Result?.ToList() ?? new List<NodeConfig>();
        }
    }
}
