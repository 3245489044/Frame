namespace Share.Conversation.ContextBinding;

/// <summary>
/// 缓存带（CacheBand）—— 提示段顺序的**单一权威**：按「变更频率升序」排列（稳定在前、易变在后），
/// 使 LLM 前缀缓存（KV cache）命中最大化。装配 / 快照 / UI 头提示层 / LLM 投喂四处顺序一律由本枚举派生。
/// <para>排序律：<c>order = (Band 升序, Band 内原声明序)</c>；前缀资格见 <see cref="CacheBandRules"/>。</para>
/// </summary>
public enum CacheBand
{
    /// <summary>B0 基底：L0（框架级系统约束、DSL 分段 <c>prompt.l0.*</c>）—— 版本级，永久前缀（最高复用）。</summary>
    B0 = 0,

    /// <summary>B1 能力：SkillBlock（技能块）、ToolBlock（工具面）—— 任务/标签级，半稳定前缀。</summary>
    B1 = 1,

    /// <summary>B2 任务：L1（任务级提示）—— 任务首步一次，任务内稳定。</summary>
    B2 = 2,

    /// <summary>B3 步骤：L2（步骤级提示）、焦点 —— 每步，步内稳定（靠后）。</summary>
    B3 = 3,

    /// <summary>B4 会话：会话历史窗口（append-only）—— 每轮追加，天然前缀复用。</summary>
    B4 = 4,

    /// <summary>B5 本轮：当前输入、工具结果、mode、subagent、时间戳/遥测 —— 每轮，**禁止进前缀（尾部）**。</summary>
    B5 = 5,

    /// <summary>B6 逐拍：挂载成员运行态（stage/round/elapsed/语义摘要）—— 每次读取即变，**恒置上下文最末**。</summary>
    B6 = 6,
}

/// <summary>段投喂目标声明 —— 段进 LLM 的槽位（system 前缀 / users 有序列表）。</summary>
public enum SegmentTarget
{
    /// <summary>LLM system 槽位（仅 B0/B1 允许进前缀）。</summary>
    System,

    /// <summary>LLM users 槽位（有序列表）。</summary>
    User,
}

/// <summary>CacheBand 语义工具（前缀资格判定；权威: 顺序框架设计 §4.1）。</summary>
public static class CacheBandRules
{
    /// <summary>是否允许进 LLM system 前缀（B0/B1）。</summary>
    public static bool IsPrefix(CacheBand band) => band is CacheBand.B0 or CacheBand.B1;

    /// <summary>是否禁止进前缀（B5 本轮易变、B6 逐拍变化；均禁入 system 前缀）。</summary>
    public static bool IsPrefixForbidden(CacheBand band) => band >= CacheBand.B5;
}
