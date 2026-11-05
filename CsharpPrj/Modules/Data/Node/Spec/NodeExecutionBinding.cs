using Modules.Node.Identity;

namespace Modules.Node.Spec;

/// <summary>
/// 节点执行声明（<b>只声明，不执行</b>）：执行器/技能/模型引用 + 行为词。
/// <para>
/// 纪律（R4/K1）：框架不解释 <see cref="Behavior"/> 词义、不持有执行闭包、不含 LLM 类型；
/// 词面由宿主声明/词表提供。
/// </para>
/// </summary>
/// <param name="Address">被声明节点地址（逻辑键 + 实例轴）。</param>
/// <param name="ExecutorKey">执行器键（宿主词表）。</param>
/// <param name="SkillRef">技能引用键。</param>
/// <param name="LlmRef">模型引用键。</param>
/// <param name="Behavior">行为词（词表驱动，框架不解释）。</param>
public sealed record NodeExecutionBinding(
    NodeAddress Address,
    string? ExecutorKey = null,
    string? SkillRef = null,
    string? LlmRef = null,
    string? Behavior = null)
{
    /// <summary>是否声明了任一执行属性（无属性 ⇒ 不参与继承解析）。</summary>
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(ExecutorKey) ||
        !string.IsNullOrWhiteSpace(SkillRef) ||
        !string.IsNullOrWhiteSpace(LlmRef) ||
        !string.IsNullOrWhiteSpace(Behavior);
}
