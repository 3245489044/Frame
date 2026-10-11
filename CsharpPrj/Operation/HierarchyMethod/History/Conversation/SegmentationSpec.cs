using System;

namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>切分参数（运行期输入，**不进声明**：声明只定"允许哪些模式、产物落哪些位置"）。</summary>
/// <param name="Mode">切分模式。</param>
/// <param name="Value">模式参数（Regex=正则；Marker=标记文本；其余忽略）。</param>
/// <param name="BudgetLimit">Budget 模式的字符上限（与上下文水位同口径）。</param>
public sealed record SegmentationSpec(SegmentationMode Mode, string? Value = null, int BudgetLimit = 0)
{
    /// <summary>模式词表校验（装载期口径）：按名解析，未识别即拒绝（不落 switch 兜底）。</summary>
    /// <param name="mode">模式名。</param>
    /// <param name="parsed">解析结果。</param>
    public static bool TryParseMode(string? mode, out SegmentationMode parsed)
        => Enum.TryParse(mode?.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
}
