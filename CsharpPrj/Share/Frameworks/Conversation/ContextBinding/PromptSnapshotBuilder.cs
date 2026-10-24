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
        IReadOnlyCollection<string>? enabledToggles = null, string? resourceBlock = null,
        int schemaVersion = PromptSnapshot.CurrentSchemaVersion)
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

        // 段集合 → 声明（L0 分段族 / 固定段），装配与绑定由通用内核（Plan → Compose）单点产出；
        // 段元数据（组/序/适用性/需求量/可空/别名/开关）全部来自声明面（PromptSegmentCatalog.Rows），
        // 本类只做「声明行 + 域内取值委托」的绑定，不再自行判空/排序/发射。
        var rows = PromptSegmentCatalog.Rows;
        var bandRow = PromptSegmentCatalog.BandTemplate;
        var specs = new List<TextSpec>(bands.Length + rows.Count);
        if (bands.Length > 0)
        {
            foreach (var band in bands)
                specs.Add(SpecOf(PromptSegmentCatalog.L0BandKey(band.Name), band.Text, bandRow));
        }
        else
        {
            var l0Row = PromptSegmentCatalog.RowOf(AnchorKey.PromptL0.Value);
            if (l0Row is not null) specs.Add(SpecOf(AnchorKey.PromptL0, l0, l0Row));
        }

        // 其余固定段：键 / 组 / 顺序 / 适用性 / 档案开关 全部由声明面**单一驱动**（新增段只改声明 ⇒ 不漏发射/漏投影）。
        var inputs = new SegmentBuildInputs(
            l0 ?? string.Empty, skillBlock ?? string.Empty, l1 ?? string.Empty, l2 ?? string.Empty,
            toolBlock, mode, subAgent, resourceBlock);
        foreach (var row in rows)
        {
            if (row.ExpandBands) continue;                                              // 分段族模板行按 bands 展开
            if (string.Equals(row.Key, AnchorKey.PromptL0.Value, StringComparison.Ordinal)) continue; // L0 由上方分段族/回退处理

            var declared = PromptSegmentCatalog.FixedSpecOf(row.Key);
            if (declared is null) continue;  // 声明了未登记取值委托的段：由 PromptDeclarationClosure 门禁收口（不静默发空段）

            specs.Add(new TextSpec(
                new TextKey(row.Key),
                string.IsNullOrWhiteSpace(row.Group) ? GroupName : row.Group,
                new OrderKey(row.Order),
                WhenVocabulary.Resolve(row.When),
                _ => BlockOf(declared.Key, declared.Produce(inputs)),
                row.Requirement,
                row.AllowEmpty,
                row.Aliases,
                row.Toggle,
                string.IsNullOrWhiteSpace(row.Provider) ? "catalog" : row.Provider));
        }

        var plan = TextPlanBuilder.Build(specs, new SegmentContext(toggles: enabledToggles));
        var assembly = TextCompose.Compose(plan, ComposePolicy.SingleGroup(GroupName));

        return new PromptSnapshot
        {
            SchemaVersion = schemaVersion,
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

    /// <summary>按声明行绑定取值委托（组/序/适用性/需求量/可空/别名/开关全部来自行；缺行 ⇒ 目录默认）。</summary>
    private static TextSpec SpecOf(AnchorKey key, string? text, SegmentDeclRow? row)
        => new(
            new TextKey(key.Value),
            string.IsNullOrWhiteSpace(row?.Group) ? GroupName : row!.Group,
            new OrderKey(row?.Order ?? (int)PromptSegmentCatalog.BandFor(key)),
            WhenVocabulary.Resolve(row?.When),
            _ => BlockOf(key, text),
            row?.Requirement ?? SegmentRequirement.Optional,
            row?.AllowEmpty ?? false,
            row?.Aliases,
            row?.Toggle,
            string.IsNullOrWhiteSpace(row?.Provider) ? "catalog" : row!.Provider);

    /// <summary>域值 → 四态：空/缺失一律显式 <see cref="TextState.Empty"/>，不再以 null 承载语义。</summary>
    private static TextBlock BlockOf(AnchorKey key, string? text)
        => string.IsNullOrEmpty(text)
            ? TextBlock.Blank(new TextKey(key.Value))
            : TextBlock.Filled(new TextKey(key.Value), text);

    /// <summary>文本 SHA256 十六进制摘要（确定性；空/非法 → 空串哈希）。权威实现 = <see cref="TextDigest.Hash"/>。</summary>
    public static string Hash(string? text) => TextDigest.Hash(text);
}
