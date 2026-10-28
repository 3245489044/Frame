namespace Share.Conversation.Rendering;

/// <summary>
/// 技能块键文法端口 —— 键形（<c>skill.{name}.block</c> / <c>skill.{name}.end</c> / <c>wf.{wf}.skill.{name}</c>）
/// 的**唯一声明面**；键值产生策略可由宿主注入（框架不持有领域策略）。
/// </summary>
public interface ISkillBlockKeyRule
{
    /// <summary>会话级头系统块键。</summary>
    string KeyForBlock(string name);

    /// <summary>skill 链末尾产出键。</summary>
    string KeyForEnd(string name);

    /// <summary>工作流中间产出键。</summary>
    string KeyForWorkflowNode(string wf, string skillName);
}

/// <summary>
/// 默认键文法实现（唯一实例；禁第二份模板复制）。
/// </summary>
public sealed class DefaultSkillBlockKeyRule : ISkillBlockKeyRule
{
    /// <summary>唯一实例（装配期单一规则实例）。</summary>
    public static readonly DefaultSkillBlockKeyRule Instance = new();

    private DefaultSkillBlockKeyRule()
    {
    }

    /// <inheritdoc />
    public string KeyForBlock(string name) => $"skill.{name}.block";

    /// <inheritdoc />
    public string KeyForEnd(string name) => $"skill.{name}.end";

    /// <inheritdoc />
    public string KeyForWorkflowNode(string wf, string skillName) => $"wf.{wf}.skill.{skillName}";
}