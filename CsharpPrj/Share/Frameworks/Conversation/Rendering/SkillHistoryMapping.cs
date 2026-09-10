using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering;

/// <summary>
/// 技能/非数据库逻辑在对话历史中的位置语义。
/// <see cref="HeadBlock"/> = 会话级头系统块（L0 之后高注意区，注入 system）；<see cref="MidNode"/> = 工作流中间产出节点；
/// <see cref="EndNode"/> = skill 驱动链的末尾产出节点（与 assistant 终答对齐）。
/// </summary>
public enum SkillHistoryPosition
{
    HeadBlock,
    MidNode,
    EndNode,
}

/// <summary>
/// 一条「非数据库逻辑 → 稳定 history 键」的映射记录（Skill 映射表的行）。
/// 把 skill（以及 L0/L1/L2 提示词等仅存在于运行期/快照、不进 ur_histories/ur_nodes 的逻辑）映射为
/// 主对话框可在消息序列中定位/展示的稳定键。键形与既有 <c>wf.</c> 前缀 + <c>nodes.{wf}.{node}</c> 约定一致。
/// </summary>
public sealed record SkillHistoryMapping(
    string TaskKey,
    int Round,
    string SkillName,
    string HistoryKey,
    SkillHistoryPosition Position,
    string? BlockText = null,
    string? Status = null)
{
    /// <summary>会话级头系统块键：skill.{name}.block。</summary>
    public static string KeyForBlock(string name) => $"skill.{name}.block";

    /// <summary>skill 链末尾产出键：skill.{name}.end。</summary>
    public static string KeyForEnd(string name) => $"skill.{name}.end";

    /// <summary>工作流中间产出键：wf.{wf}.skill.{name}。</summary>
    public static string KeyForWorkflowNode(string wf, string skillName) => $"wf.{wf}.skill.{skillName}";
}

/// <summary>
/// 技能历史映射表（SkillHistoryMapper）——把技能等非数据库逻辑映射为稳定 history 键的推导器。
/// 纯逻辑、零 Agent 依赖，可单测。输入为技能名集合（来自 <see cref="SkillInjector"/> 选中的 SelectedSkills，
/// 或 SkillRuntimeState 全量），输出为该任务的映射行（头部块 + 末尾产出两条稳定键）。
/// </summary>
public static class SkillHistoryMapper
{
    /// <summary>由技能名集合推导映射行：每个技能 → 头部块 + 末尾产出两个 history 键（去重、稳定序）。</summary>
    public static IReadOnlyList<SkillHistoryMapping> ResolveMappings(
        string taskKey, int round, IReadOnlyList<string> skillNames)
    {
        if (skillNames is null || skillNames.Count == 0) return Array.Empty<SkillHistoryMapping>();
        var list = new List<SkillHistoryMapping>(skillNames.Count * 2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in skillNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
            list.Add(new SkillHistoryMapping(taskKey, round, name, SkillHistoryMapping.KeyForBlock(name), SkillHistoryPosition.HeadBlock));
            list.Add(new SkillHistoryMapping(taskKey, round, name, SkillHistoryMapping.KeyForEnd(name), SkillHistoryPosition.EndNode));
        }
        return list;
    }
}
