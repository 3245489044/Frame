using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Operation.HierarchyMethod.History.Ports;

namespace Operation.HierarchyMethod.History.Conversation;

/// <summary>
/// 历史切分器（**纯函数**：<c>中性条目 + SegmentationSpec → ConversationSegment[]</c>）—— 域无关的分组引擎。
/// <list type="bullet">
/// <item>输入 = 宿主经 <see cref="IConversationItemReader"/> 映射的中性条目（不感知任何宿主消息形态）；</item>
/// <item>不落库、不碰位置（写侧经内容物化服务 → 唯一写门）；</item>
/// <item>段 <c>seq</c> 源索引派生（幂等）；<c>parent</c> 为键引用（经 <see cref="IHistoryKeyGrammar"/>）。</item>
/// </list>
/// <para>
/// 契约：<see cref="ConversationItem.Index"/> 须等于其在 <paramref name="items"/> 中的零基位置（宿主 1:1 映射）。
/// </para>
/// </summary>
public static class HistorySegmenter
{
    /// <summary>
    /// 切分一轮中性条目。空输入 → 空段集（不抛）；模式参数缺失（Regex/Marker 无 Value、Budget 无上限）→ 抛
    /// <see cref="ArgumentException"/>（显式拒绝，不落兜底语义）。
    /// </summary>
    /// <param name="items">该轮中性条目（按源序）。</param>
    /// <param name="spec">切分参数（模式 + 模式参数）。</param>
    /// <param name="container">容器段（会话名 / taskKey），构成父键引用。</param>
    /// <param name="round">轮次（从 1 起）。</param>
    /// <param name="grammar">键文法（父键引用构造）。</param>
    public static IReadOnlyList<ConversationSegment> Segment(
        IReadOnlyList<ConversationItem> items, SegmentationSpec spec, string container, int round,
        IHistoryKeyGrammar grammar)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(grammar);
        Validate(spec);

        if (items.Count == 0)
        {
            return Array.Empty<ConversationSegment>();
        }

        var parent = grammar.ParentKey(container, round);
        var array = items as ConversationItem[] ?? items.ToArray();

        // iteration = 该条目所属的 LLM 回合序（assistant/say 命中即开启新回合；回合前条目归 0）。
        var iterations = new int[array.Length];
        var iteration = 0;
        for (var index = 0; index < array.Length; index++)
        {
            if (array[index].IsAssistantOutput)
            {
                iteration++;
            }

            iterations[index] = iteration;
        }

        var groups = spec.Mode switch
        {
            SegmentationMode.Message => array.Select(i => new[] { i }).ToArray(),
            SegmentationMode.Iteration => GroupByIteration(array),
            SegmentationMode.Tool => GroupByTool(array),
            SegmentationMode.Role => GroupByRole(array),
            SegmentationMode.Regex => GroupByRegex(array, spec.Value!),
            SegmentationMode.Budget => GroupByBudget(array, spec.BudgetLimit),
            SegmentationMode.Marker => GroupByMarker(array, spec.Value!),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Mode, "未声明的切分模式"),
        };

        var segments = new List<ConversationSegment>(groups.Length);
        foreach (var group in groups)
        {
            if (group.Length == 0)
            {
                continue;
            }

            var first = group[0];
            segments.Add(new ConversationSegment(
                Seq: first.Index + 1,
                Kind: KindOf(spec.Mode, group),
                Role: first.Role,
                Text: JoinText(group),
                Ts: first.Ts,
                Iteration: iterations[first.Index],
                ToolCallId: first.ToolCallId,
                SourceIndex: first.Index,
                Parent: parent));
        }

        return segments;
    }

    /// <summary>内容摘要（SHA-256 前 16 字节 hex；稳定、可对账，非安全用途）。</summary>
    /// <param name="payload">待摘要文本。</param>
    public static string ComputeDigest(string payload)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty)))[..32];

    private static void Validate(SegmentationSpec spec)
    {
        switch (spec.Mode)
        {
            case SegmentationMode.Regex:
            case SegmentationMode.Marker:
                if (string.IsNullOrWhiteSpace(spec.Value))
                {
                    throw new ArgumentException($"切分模式 {spec.Mode} 需给出模式参数 value（不落兜底语义）", nameof(spec));
                }

                break;
            case SegmentationMode.Budget:
                if (spec.BudgetLimit <= 0)
                {
                    throw new ArgumentException("切分模式 Budget 需给出正数 budget_limit（字符上限）", nameof(spec));
                }

                break;
        }
    }

    /// <summary>段语义类型：Message=原消息类型；Role=角色名；Tool=调用/结果；其余=分组语义名。</summary>
    private static string KindOf(SegmentationMode mode, ConversationItem[] group) => mode switch
    {
        SegmentationMode.Message => group[0].Type,
        SegmentationMode.Role => group[0].Role,
        SegmentationMode.Tool => group[0].IsToolResult ? "tool_result" : "tool_call",
        SegmentationMode.Iteration => "iteration",
        SegmentationMode.Budget => "budget",
        SegmentationMode.Regex => "regex",
        SegmentationMode.Marker => "marker",
        _ => "segment",
    };

    private static ConversationItem[][] GroupByIteration(ConversationItem[] items)
    {
        var groups = new List<ConversationItem[]>();
        var current = new List<ConversationItem>();
        foreach (var item in items)
        {
            // assistant/say = LLM 回合产出：命中处开启新段（首条即便命中也不产生空段）。
            if (item.IsAssistantOutput && current.Count > 0)
            {
                groups.Add(current.ToArray());
                current = new List<ConversationItem>();
            }

            current.Add(item);
        }

        if (current.Count > 0)
        {
            groups.Add(current.ToArray());
        }

        return groups.ToArray();
    }

    private static ConversationItem[][] GroupByTool(ConversationItem[] items)
    {
        var groups = new List<ConversationItem[]>();
        var current = new List<ConversationItem>();
        foreach (var item in items)
        {
            if (item.IsToolResult && current.Count > 0)
            {
                current.Add(item);      // 结果并入其调用段（调用→结果成对）
                groups.Add(current.ToArray());
                current = new List<ConversationItem>();
                continue;
            }

            if (item.IsToolCall && current.Count > 0)
            {
                groups.Add(current.ToArray());  // 上一次未配对调用：单独成段
                current = new List<ConversationItem>();
            }

            current.Add(item);
        }

        if (current.Count > 0)
        {
            groups.Add(current.ToArray());
        }

        return groups.ToArray();
    }

    private static ConversationItem[][] GroupByRole(ConversationItem[] items)
    {
        var groups = new List<ConversationItem[]>();
        var current = new List<ConversationItem>();
        string? role = null;
        foreach (var item in items)
        {
            if (role is not null && !string.Equals(role, item.Role, StringComparison.Ordinal))
            {
                groups.Add(current.ToArray());
                current = new List<ConversationItem>();
            }

            role = item.Role;
            current.Add(item);
        }

        if (current.Count > 0)
        {
            groups.Add(current.ToArray());
        }

        return groups.ToArray();
    }

    private static ConversationItem[][] GroupByRegex(ConversationItem[] items, string pattern)
    {
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        var groups = new List<ConversationItem[]>();
        var current = new List<ConversationItem>();
        foreach (var item in items)
        {
            if (regex.IsMatch(item.Text) && current.Count > 0)
            {
                groups.Add(current.ToArray());
                current = new List<ConversationItem>();
            }

            current.Add(item);
        }

        if (current.Count > 0)
        {
            groups.Add(current.ToArray());
        }

        return groups.ToArray();
    }

    private static ConversationItem[][] GroupByBudget(ConversationItem[] items, int budgetLimit)
    {
        var groups = new List<ConversationItem[]>();
        var current = new List<ConversationItem>();
        var used = 0;
        foreach (var item in items)
        {
            if (current.Count > 0 && used + item.Text.Length > budgetLimit)
            {
                groups.Add(current.ToArray());
                current = new List<ConversationItem>();
                used = 0;
            }

            current.Add(item);
            used += item.Text.Length;
        }

        if (current.Count > 0)
        {
            groups.Add(current.ToArray());
        }

        return groups.ToArray();
    }

    private static ConversationItem[][] GroupByMarker(ConversationItem[] items, string marker)
    {
        var groups = new List<ConversationItem[]>();
        var current = new List<ConversationItem>();
        foreach (var item in items)
        {
            if (item.Text.Contains(marker, StringComparison.Ordinal) && current.Count > 0)
            {
                groups.Add(current.ToArray());
                current = new List<ConversationItem>();
            }

            current.Add(item);
        }

        if (current.Count > 0)
        {
            groups.Add(current.ToArray());
        }

        return groups.ToArray();
    }

    private static string JoinText(ConversationItem[] group)
        => group.Length == 1
            ? group[0].Text
            : string.Join("\n", group.Select(i => i.Text).Where(t => t.Length > 0));
}
