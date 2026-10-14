#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 提示词段目录（PromptSegmentCatalog）—— 段的**有序唯一注册表**（含 L0 分段族）。
/// <para>
/// 快照构建（<see cref="PromptSnapshotBuilder"/>）/ 段投影（UI 与 LLM 头提示层）/ 校验（<see cref="ContextBindingValidator"/>）
/// / 渲染（<c>prompt:*</c> 折叠气泡）一律由本目录驱动；新增段只改本目录一处。
/// </para>
/// <para>完备性判据见 <see cref="Validate"/>（UiStage/Produce/Text 缺一即不合规，可机械复算）。</para>
/// </summary>
public static class PromptSegmentCatalog
{
    /// <summary>档案开关名：技能块段。</summary>
    public const string ToggleSkills = "skills";

    /// <summary>档案开关名：模式声明段。</summary>
    public const string ToggleMode = "mode";

    /// <summary>档案开关名：工具面段。</summary>
    public const string ToggleTools = "tools";

    /// <summary>L0 分段键前缀（<c>prompt.l0.&lt;技能名&gt;</c>）。</summary>
    public const string L0BandPrefix = "prompt.l0.";

    /// <summary>
    /// 固定段（有序；L0 分段族除外）。顺序 = 头提示层展示顺序。
    /// <para>键 / stage / 写侧取值 / 读侧取值 / 档案开关 均在此单点声明。</para>
    /// </summary>
    public static readonly IReadOnlyList<SegmentSpec> Fixed = new[]
    {
        new SegmentSpec(AnchorKey.PromptL0, "prompt:l0", i => i.L0, s => s.L0),
        new SegmentSpec(AnchorKey.PromptL1, "prompt:l1", i => i.L1, s => s.L1),
        new SegmentSpec(AnchorKey.PromptL2, "prompt:l2", i => i.L2, s => s.L2),
        new SegmentSpec(AnchorKey.SkillCombined, "prompt:skill.block", i => i.SkillBlock, s => s.SkillBlock, ToggleSkills),
        new SegmentSpec(AnchorKey.ToolCombined, "prompt:tools", i => i.ToolBlock, s => s.ToolBlock, ToggleTools),
        new SegmentSpec(AnchorKey.PromptMode, "prompt:mode", i => i.Mode, s => s.Mode, ToggleMode),
        new SegmentSpec(AnchorKey.PromptSubAgent, "prompt:subagent", i => i.SubAgent, s => s.SubAgent),
    };

    /// <summary>是否 L0 分段键（<c>prompt.l0.&lt;技能名&gt;</c>）。</summary>
    public static bool IsL0Band(AnchorKey key) => key.Value.StartsWith(L0BandPrefix, StringComparison.Ordinal);

    /// <summary>L0 分段键 → 技能名。</summary>
    public static string L0BandName(AnchorKey key) => key.Value.Substring(L0BandPrefix.Length);

    /// <summary>L0 分段展示 stage（<c>prompt:l0:&lt;技能名&gt;</c>）。</summary>
    public static string L0BandStage(string name) => "prompt:l0:" + name;

    /// <summary>L0 分段键构造。</summary>
    public static AnchorKey L0BandKey(string name) => AnchorKey.PromptL0Band(name);

    /// <summary>段键 → 展示 stage（未知 → false）。</summary>
    public static bool TryStage(AnchorKey key, out string stage)
    {
        if (IsL0Band(key)) { stage = L0BandStage(L0BandName(key)); return true; }
        foreach (var spec in Fixed)
        {
            if (spec.Key == key) { stage = spec.UiStage; return true; }
        }
        stage = string.Empty;
        return false;
    }

    /// <summary>段键 + 快照 → 段文本（未知 → false）。</summary>
    public static bool TryText(AnchorKey key, PromptSnapshot snapshot, out string text)
    {
        if (IsL0Band(key))
        {
            var name = L0BandName(key);
            text = snapshot.L0Bands.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.Ordinal))?.Text ?? string.Empty;
            return true;
        }
        foreach (var spec in Fixed)
        {
            if (spec.Key == key) { text = spec.Text(snapshot); return true; }
        }
        text = string.Empty;
        return false;
    }

    /// <summary>档案开关集（按 profile 的 IncludeSkills/IncludeMode/IncludeTools 归一出启用开关名）。</summary>
    public static IReadOnlyCollection<string> Toggles(bool includeSkills, bool includeMode, bool includeTools)
    {
        var set = new List<string>(3);
        if (includeSkills) set.Add(ToggleSkills);
        if (includeMode) set.Add(ToggleMode);
        if (includeTools) set.Add(ToggleTools);
        return set;
    }

    /// <summary>段完备性判据（可机械复算）：每固定段须同时具备 UiStage/Produce/Text；缺一即违规。</summary>
    public static IReadOnlyList<string> Validate()
    {
        var violations = new List<string>();
        foreach (var spec in Fixed)
        {
            if (string.IsNullOrWhiteSpace(spec.UiStage)) violations.Add($"段 {spec.Key.Value} 缺 UiStage");
            if (spec.Produce is null) violations.Add($"段 {spec.Key.Value} 缺 Produce（写侧取值）");
            if (spec.Text is null) violations.Add($"段 {spec.Key.Value} 缺 Text（读侧取值）");
        }
        return violations;
    }
}
