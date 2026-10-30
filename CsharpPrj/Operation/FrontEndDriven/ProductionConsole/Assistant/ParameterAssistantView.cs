using System.Collections.Generic;

namespace FrontEndDriven.ProductionConsole.Assistant;

/// <summary>助理区域渲染视图（不可变投影；渲染层只读本对象，不触状态）。</summary>
public sealed class ParameterAssistantView
{
    /// <summary>自然语言输入回填值。</summary>
    public string? Prompt { get; init; }

    /// <summary>待应用提案（仅建议键）。</summary>
    public IReadOnlyDictionary<string, object?>? PendingProposal { get; init; }

    /// <summary>提案错误文案。</summary>
    public string? ProposalError { get; init; }

    /// <summary>是否已接入提案来源（false → 解析按钮禁用并给出接入提示）。</summary>
    public bool IsAvailable { get; init; }

    /// <summary>是否有待应用提案（决定「应用」按钮可用性）。</summary>
    public bool HasPending => PendingProposal is { Count: > 0 };
}
