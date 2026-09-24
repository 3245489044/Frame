#region USINGS
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.DefaultImplementations;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
#endregion

namespace WorkFlowMethod.GraphFramework.Deduplication
{
    /// <summary>
    /// 链合并器工厂（typed 链路径的<b>唯一构造点</b>）：合并器/键提取器/校验器/合并策略一律经此产出，
    /// 禁各消费点各 new 一套（TH-12 单一实现，治重复实现）。键提取 = 节点 <c>Callkey</c>。
    /// </summary>
    public static class ChainComposerFactory
    {
        public static IChainComposer<NodeConfig> Create() =>
            new DefaultChainComposer<NodeConfig>(
                validator: new DefaultCompositionValidator(),
                keyExtractor: node => node.basicConfig?.Callkey,
                mergeStrategy: new DefaultMergeStrategy<NodeConfig>());
    }
}
