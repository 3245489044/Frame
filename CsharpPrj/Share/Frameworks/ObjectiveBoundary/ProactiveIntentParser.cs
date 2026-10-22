using System;
using Share.ObjectiveBoundary;


namespace Share.ObjectiveBoundary;

/// <summary>
/// 主动式意图解析（皮层主动触发信号源之一）：从执行智能体自评文本（ClineResult.Text）识别边界动作意图。
/// 关键词规则与 rule_match 的 L2ReplanConditions（"需要语义重规划/方案需重设计/方向错误/此路不通"）对齐。
/// 关键词词汇表为字符串逻辑主体，作为公理 <see cref="Plate.Axioms.L2.BoundaryIntentClassification"/> 的绑定实现
/// （公理声明权威见 skills/rare/boundaries/SKILL.md §四，反转生成于 Agent/Axioms/L2）。
/// 产出原始 ProactiveBoundaryIntent（不做过滤；白名单/置信度门控由 ProactiveBoundaryTrigger 负责）。
/// </summary>
public static class ProactiveIntentParser
{
    private static readonly (string Action, double Confidence, string[] Markers)[] Rules =
    {
        (BoundaryActionKind.Compact, 0.7, new[]
        {
            "需要压缩上下文", "压缩上下文", "上下文过大", "上下文超长", "压缩历史", "context 过大", "context 太长", "上下文接近极限",
        }),
        (BoundaryActionKind.ResubmitWithInfo, 0.7, new[]
        {
            "需要更多信息", "缺少必要信息", "需要补充信息", "补充日志", "需要新的输入", "缺少输入条件", "需要外部信息", "缺少日志",
        }),
        (BoundaryActionKind.Rescope, 0.6, new[]
        {
            "此路不通", "方案需重设计", "方向错误", "需要语义重规划", "无法完成", "超出当前边界", "子问题不可行",
        }),
        (BoundaryActionKind.Escalate, 0.5, new[]
        {
            "需要更高预算", "需要升级", "预算不足", "需要更换模型",
        }),
    };

    public static ProactiveBoundaryIntent? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string? bestAction = null;
        double bestConfidence = 0d;
        foreach (var (action, confidence, markers) in Rules)
        {
            foreach (var marker in markers)
            {
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 && confidence > bestConfidence)
                {
                    bestAction = action;
                    bestConfidence = confidence;
                }
            }
        }

        return bestAction is null ? null : new ProactiveBoundaryIntent(bestAction, "text", bestConfidence);
    }

    /// <summary>由 L2 生成器结构化输出（boundary_action 字段）构造主动式意图；动作非法/空时返回 null。</summary>
    public static ProactiveBoundaryIntent? FromLlmAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        var a = action.Trim();
        if (!IsKnownAction(a)) return null;
        return new ProactiveBoundaryIntent(a, "llm", 0.8);
    }

    private static bool IsKnownAction(string action)
        => string.Equals(action, BoundaryActionKind.Compact, StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, BoundaryActionKind.ResubmitWithInfo, StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, BoundaryActionKind.Rescope, StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, BoundaryActionKind.Escalate, StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, BoundaryActionKind.ExpandRoot, StringComparison.OrdinalIgnoreCase);
}
