using System.Collections.Generic;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>
/// AI 参数助理状态（唯一持有者）：待应用提案绑定能力 id，换能力即失效；
/// <c>Prompt</c> 仅用于重渲染回填，不进入任务参数。
/// </summary>
public sealed class ParameterAssistantState
{
    /// <summary>pending 所属能力 id（apply 前校验；空 = 尚未解析）。</summary>
    public string? CapabilityId { get; set; }

    /// <summary>自然语言输入原文。</summary>
    public string? Prompt { get; set; }

    /// <summary>待应用提案（仅含能力声明的键，值已按控件形态归一）。</summary>
    public Dictionary<string, object?>? PendingProposal { get; set; }

    /// <summary>提案失败原因（未接入/超时/解析失败/校验失败）。</summary>
    public string? ProposalError { get; set; }

    /// <summary>最近一次应用条数（用于提示文案）。</summary>
    public int AppliedCount { get; set; }
}
