using System;
using System.Collections.Generic;
using System.Linq;
using Share.Composition.Text;

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

        // 段集合 → 声明（L0 分段族 / 固定段），装配与绑定由通用内核（Plan → Compose）单点产出，
        // 本类只做「提示词域声明 → 通用段声明」的映射，不再自行判空/排序/发射。
        var specs = new List<TextSpec>(bands.Length + PromptSegmentCatalog.Fixed.Count);
        if (bands.Length > 0)
        {
            foreach (var band in bands)
                specs.Add(SpecOf(PromptSegmentCatalog.L0BandKey(band.Name), band.Text));
        }
        else
        {
            specs.Add(SpecOf(AnchorKey.PromptL0, l0));
        }

        // 其余固定段：键 / 顺序 / 档案开关 全部由段目录**单一驱动**（新增段只改目录 ⇒ 不再漏发射/漏投影）。
        var inputs = new SegmentBuildInputs(
            l0 ?? string.Empty, skillBlock ?? string.Empty, l1 ?? string.Empty, l2 ?? string.Empty,
            toolBlock, mode, subAgent, resourceBlock);
        foreach (var spec in PromptSegmentCatalog.Fixed)
        {
            if (spec.Key == AnchorKey.PromptL0) continue; // L0 由上方分段族/回退处理
            var declared = spec;
            specs.Add(new TextSpec(
                new TextKey(declared.Key.Value),
                GroupName,
                new OrderKey((int)declared.Band),
                static _ => true,
                _ => BlockOf(declared.Key, declared.Produce(inputs)),
                Toggle: declared.Toggle));
        }

        var plan = TextPlanBuilder.Build(specs, new SegmentContext(toggles: enabledToggles));
        var assembly = TextCompose.Compose(plan, ComposePolicy.SingleGroup(GroupName));

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
            // 顺序单一权威 = 内核（OrderKey 升序 + 声明序稳定）；本类不再二次排序。
            Bindings = assembly.Bindings
                .Where(static b => b.State == TextState.Known)
                .Select(static b => new MessageBinding(
                    new AnchorKey(b.Key.Value), MessageOrigin.LlmSystem, FeedTarget.System, b.TextHash))
                .ToList(),
        };
    }

    /// <summary>提示词段所属的通用组标签（唯一出现组语义的地方）。</summary>
    private const string GroupName = "system";

    /// <summary>按段目录的缓存带映射通用序键（顺序语义留在域内）。</summary>
    private static TextSpec SpecOf(AnchorKey key, string? text)
        => new(
            new TextKey(key.Value),
            GroupName,
            new OrderKey((int)PromptSegmentCatalog.BandFor(key)),
            static _ => true,
            _ => BlockOf(key, text));

    /// <summary>域值 → 四态：空/缺失一律显式 <see cref="TextState.Empty"/>，不再以 null 承载语义。</summary>
    private static TextBlock BlockOf(AnchorKey key, string? text)
        => string.IsNullOrEmpty(text)
            ? TextBlock.Blank(new TextKey(key.Value))
            : TextBlock.Filled(new TextKey(key.Value), text);

    /// <summary>文本 SHA256 十六进制摘要（确定性；空/非法 → 空串哈希）。权威实现 = <see cref="TextDigest.Hash"/>。</summary>
    public static string Hash(string? text) => TextDigest.Hash(text);
}
