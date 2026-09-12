using System.Linq;
using ParameterResolver.KeyMapping;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点级 / 全局级 KeyMapping 优先级合并（单一实现）：全局规则降偏移，节点级规则优先命中（T5）。
/// 自 Agent 侧 <c>DataContainerBinder.MergeMappings</c> 上移（该处改为委托本类），供框架节点执行服务与 Agent 共用同一口径。
/// </summary>
public static class KeyMappingMerge
{
    /// <summary>节点级 vs 全局级优先级的隔离偏移：节点级规则优先命中（T5）。</summary>
    private const int GlobalPriorityOffset = 1000;

    /// <summary>优先级合并：全局规则降偏移（节点级优先命中，T5）。</summary>
    public static KeyMappingConfig MergeMappings(KeyMappingConfig? node, KeyMappingConfig? global)
    {
        var merged = new KeyMappingConfig
        {
            WildcardChar = global?.WildcardChar ?? node?.WildcardChar ?? "*",
            DefaultSeparator = global?.DefaultSeparator ?? node?.DefaultSeparator ?? ".",
            EnableHierarchyNormalization = global?.EnableHierarchyNormalization ?? node?.EnableHierarchyNormalization ?? true,
        };
        if (global != null)
            merged.Rules.AddRange(global.Rules.Select(r => CloneWithOffset(r, GlobalPriorityOffset)));
        if (node != null)
            merged.Rules.AddRange(node.Rules);
        return merged;
    }

    private static MappingRule CloneWithOffset(MappingRule rule, int offset) => new()
    {
        RuleType = rule.RuleType,
        Priority = rule.Priority + offset,
        SourcePattern = rule.SourcePattern,
        TargetPattern = rule.TargetPattern,
        LevelSeparator = rule.LevelSeparator,
        RequireAllLevels = rule.RequireAllLevels,
        MatchAllSourceKeys = rule.MatchAllSourceKeys,
        LevelPatterns = rule.LevelPatterns.Select(lp => new LevelPattern
        {
            TargetPattern = lp.TargetPattern,
            SourcePattern = lp.SourcePattern,
            IsRequired = lp.IsRequired,
        }).ToList(),
    };
}
