using System;
using System.Collections.Generic;
using Share.Conversation.Rendering;

namespace Share.Conversation.Rendering;

/// <summary>
/// 技能历史映射表 —— 把技能等非数据库逻辑映射为稳定会话键的推导器（Agent 侧位置策略）。
/// 键文法经框架端口 <see cref="ISkillBlockKeyRule"/>（缺省 <see cref="DefaultSkillBlockKeyRule"/>），
/// DTO <see cref="SkillBlockMapping"/> 与枚举 <see cref="SkillPosition"/> 仍属框架纯 DTO 面。
/// </summary>
public static class SkillHistoryMapper
{
    /// <summary>由技能名集合推导映射行：每个技能 → 头部块 + 末尾产出两条稳定键（去重、稳定序）。</summary>
    public static IReadOnlyList<SkillBlockMapping> ResolveMappings(
        string taskKey, int round, IReadOnlyList<string> skillNames)
        => ResolveMappings(taskKey, round, skillNames, DefaultSkillBlockKeyRule.Instance);

    /// <summary>同上，键文法规则可注入（单一规则实例，禁双份模板）。</summary>
    public static IReadOnlyList<SkillBlockMapping> ResolveMappings(
        string taskKey, int round, IReadOnlyList<string> skillNames, ISkillBlockKeyRule rule)
    {
        if (skillNames is null || skillNames.Count == 0) return Array.Empty<SkillBlockMapping>();
        rule ??= DefaultSkillBlockKeyRule.Instance;
        var list = new List<SkillBlockMapping>(skillNames.Count * 2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in skillNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
            list.Add(new SkillBlockMapping(taskKey, round, name, rule.KeyForBlock(name), SkillPosition.HeadBlock));
            list.Add(new SkillBlockMapping(taskKey, round, name, rule.KeyForEnd(name), SkillPosition.EndNode));
        }
        return list;
    }
}