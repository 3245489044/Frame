namespace Share.Conversation;

/// <summary>
/// 消息呈现通道 —— 决定一条规范消息在 UI 走「对话气泡流」还是「操作流」。
/// <para>
/// 与 <see cref="ContextBinding.FeedTarget"/>（投喂目标：进不进 LLM）<b>正交</b>：
/// FeedTarget 管「进 LLM 的哪个槽位」，Channel 管「用哪种 UI 面呈现」。
/// </para>
/// <para>
/// 分类单一来源 = 写侧按本轮声明模式（declaredMode）裁决（<c>OperationModeCatalog</c>）；
/// 读侧/渲染侧只读本字段，禁按 Role 二次猜测（消除「操作命令 → 对话气泡」的呈现错配）。
/// </para>
/// </summary>
public enum MessageChannel
{
    /// <summary>对话气泡（user / assistant / LLM 过程节点）。</summary>
    Dialogue = 0,

    /// <summary>操作流（task create/activate/stop/settle 等状态变更命令与回执；非气泡呈现）。</summary>
    Operation = 1,

    /// <summary>过程流（工具/阶段/LLM 等观测帧与节点产出；不进对话气泡，由节点面承载）。</summary>
    Process = 2,
}
