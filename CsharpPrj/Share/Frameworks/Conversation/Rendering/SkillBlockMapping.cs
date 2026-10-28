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
public sealed record SkillBlockMapping(
    string TaskKey,
    int Round,
    string SkillName,
    string HistoryKey,
    SkillHistoryPosition Position,
    string? BlockText = null,
    string? Status = null);
