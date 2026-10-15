using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 写侧快照构建器 —— 纯函数，由装配产出的 L0/L1/L2/SkillBlock/SelectedSkills 构建带 Binding 的
/// <see cref="PromptSnapshot"/>，并计算各段 SHA256 文本哈希（供 I3 system 逐字哈希一致校验）。
/// 零 Agent 依赖，可单测。
/// </summary>
public static class PromptSnapshotBuilder
{
    public static PromptSnapshot Build(
        string l0, string skillBlock, string l1, string l2,
        IReadOnlyList<string>? selectedSkills,
        string? toolBlock = null, IReadOnlyList<string>? selectedTools = null, string? mode = null,
        IReadOnlyList<PromptBand>? l0Bands = null, string? subAgent = null,
        IReadOnlyCollection<string>? enabledToggles = null, string? resourceBlock = null)
    {
        var skills = (selectedSkills ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tools = (selectedTools ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        // L0 分段：保留技能名 + 正文（去空段）；空 = 未分段（回退单一 prompt.l0）。
        var bands = (l0Bands ?? Array.Empty<PromptBand>())
            .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Text))
            .ToArray();

        var bindings = new List<MessageBinding>(8);
        // L0 段：分段时逐段发射 prompt.l0.<技能名>（UI 一文件一气泡）；未分段回退单一 prompt.l0。
        if (bands.Length > 0)
        {
            foreach (var band in bands)
                Add(bindings, PromptSegmentCatalog.L0BandKey(band.Name), band.Text);
        }
        else
        {
            Add(bindings, AnchorKey.PromptL0, l0);
        }

        // 其余固定段：键 / 顺序 / 档案开关 全部由段目录**单一驱动**（新增段只改目录 ⇒ 不再漏发射/漏投影）。
        var inputs = new SegmentBuildInputs(
            l0 ?? string.Empty, skillBlock ?? string.Empty, l1 ?? string.Empty, l2 ?? string.Empty,
            toolBlock, mode, subAgent, resourceBlock);
        foreach (var spec in PromptSegmentCatalog.Fixed)
        {
            if (spec.Key == AnchorKey.PromptL0) continue; // L0 由上方分段族/回退处理
            if (spec.Toggle is not null && enabledToggles is not null && !enabledToggles.Contains(spec.Toggle))
                continue;
            Add(bindings, spec.Key, spec.Produce(inputs));
        }

        return new PromptSnapshot
        {
            L0 = l0 ?? string.Empty,
            SkillBlock = skillBlock ?? string.Empty,
            L1 = l1 ?? string.Empty,
            L2 = l2 ?? string.Empty,
            SelectedSkills = skills,
            L0Bands = bands,
            ToolBlock = toolBlock ?? string.Empty,
            SelectedTools = tools,
            Mode = mode ?? string.Empty,
            SubAgent = subAgent ?? string.Empty,
            ResourceBlock = resourceBlock ?? string.Empty,
            // 顺序单一权威：按 CacheBand 升序稳定排序（B0 分段族 → B1 能力 → B2/B3 → B5 本轮）；
            // OrderBy 稳定 ⇒ Band 内保持原声明序（L0 分段族保文件序）。
            Bindings = bindings.OrderBy(b => PromptSegmentCatalog.BandFor(b.Key)).ToList(),
        };
    }

    private static void Add(List<MessageBinding> bindings, AnchorKey key, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        bindings.Add(new MessageBinding(key, MessageOrigin.LlmSystem, FeedTarget.System, Hash(text)));
    }

    /// <summary>文本 SHA256 十六进制摘要（确定性；空/非法 → 空串哈希）。</summary>
    public static string Hash(string? text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)));
}
