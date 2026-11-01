#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Share.Composition.Text;

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

    /// <summary>档案开关名：运行时资源段（分级索引档；关闭 ⇒ 回退旧整块/不注入资源段）。</summary>
    public const string ToggleResources = "resources";

    /// <summary>L0 分段键前缀（<c>prompt.l0.&lt;技能名&gt;</c>）。</summary>
    public const string L0BandPrefix = "prompt.l0.";

    /// <summary>L0 分段展示 stage 前缀（<c>prompt:l0:</c>；段目录单源）。</summary>
    public const string L0BandStagePrefix = "prompt:l0:";


    /// <summary>
    /// 固定段（有序；L0 分段族除外）。顺序 = 头提示层展示顺序 = **CacheBand 升序**（变更频率升序：稳定在前、易变在后）。
    /// <para>键 / stage / 写侧取值 / 读侧取值 / 档案开关 / CacheBand / Target 均在此单点声明（顺序单一权威）。</para>
    /// </summary>
    public static readonly IReadOnlyList<SegmentSpec> Fixed = new[]
    {
        new SegmentSpec(AnchorKey.PromptL0, "prompt:l0", i => i.L0, s => s.L0, Band: CacheBand.B0),
        new SegmentSpec(AnchorKey.SkillCombined, "prompt:skill.block", i => i.SkillBlock, s => s.SkillBlock, ToggleSkills, CacheBand.B1, SegmentTarget.System),
        new SegmentSpec(AnchorKey.ToolCombined, "prompt:tools", i => i.ToolBlock, s => s.ToolBlock, ToggleTools, CacheBand.B1, SegmentTarget.System),
        new SegmentSpec(AnchorKey.PromptResources, "prompt:resources", i => i.ResourceBlock, s => s.ResourceBlock, ToggleResources, CacheBand.B2, SegmentTarget.User),
        new SegmentSpec(AnchorKey.PromptL1, "prompt:l1", i => i.L1, s => s.L1, Band: CacheBand.B2, Target: SegmentTarget.User),
        new SegmentSpec(AnchorKey.PromptL2, "prompt:l2", i => i.L2, s => s.L2, Band: CacheBand.B3, Target: SegmentTarget.User),
        new SegmentSpec(AnchorKey.PromptMode, "prompt:mode", i => i.Mode, s => s.Mode, ToggleMode, CacheBand.B5, SegmentTarget.User),
        new SegmentSpec(AnchorKey.PromptSubAgent, "prompt:subagent", i => i.SubAgent, s => s.SubAgent, Band: CacheBand.B5, Target: SegmentTarget.User),
    };

    /// <summary>L0 分段族模板行键（<c>expand=band</c>）。</summary>
    public const string BandTemplateKey = "prompt.l0.band";

    private static readonly object DeclaredGate = new();
    private static IReadOnlyList<SegmentDeclRow>? _declared;

    /// <summary>
    /// 段声明行（声明面唯一事实）—— 未注入声明（<see cref="Override"/>）时由 <see cref="Fixed"/>
    /// 派生等价行（<b>行为逐字不变</b>），注入后以声明面为准。
    /// </summary>
    public static IReadOnlyList<SegmentDeclRow> Rows => _declared ?? DefaultRows;

    /// <summary>由段目录派生的默认声明行（回退路径；含 L0 分段族模板行）。</summary>
    public static IReadOnlyList<SegmentDeclRow> DefaultRows { get; } = BuildDefaultRows();

    private static IReadOnlyList<SegmentDeclRow> BuildDefaultRows()
    {
        var rows = new List<SegmentDeclRow>(Fixed.Count + 1);
        foreach (var spec in Fixed)
        {
            rows.Add(new SegmentDeclRow(
                spec.Key.Value, "system", (int)spec.Band,
                WhenVocabulary.Always, SegmentRequirement.Optional,
                AllowEmpty: false, Aliases: LegacyAliases(spec.Key.Value), Toggle: spec.Toggle, Provider: "catalog"));
        }

        rows.Add(new SegmentDeclRow(
            BandTemplateKey, "system", (int)CacheBand.B0,
            WhenVocabulary.Always, SegmentRequirement.Optional,
            AllowEmpty: false, Aliases: null, Toggle: null, Provider: "catalog",
            ExpandBands: true));
        return rows;
    }

    /// <summary>回退口径（声明面缺失时的等价别名）：与 <c>prompt-segments.ushar</c> 声明同义，声明可覆盖。</summary>
    private static IReadOnlyList<string>? LegacyAliases(string key) => key switch
    {
        "prompt.l1" => new[] { "session_input" },
        "prompt.l2" => new[] { "session_window", "prompt" },
        _ => null,
    };

    /// <summary>
    /// 注入声明面（组合根调用一次）。重复注入不同内容 ⇒ 抛（声明必须是单源，禁运行期漂移）；
    /// 相同内容 ⇒ 幂等。
    /// </summary>
    public static void Override(IReadOnlyList<SegmentDeclRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        lock (DeclaredGate)
        {
            if (_declared is not null)
            {
                if (_declared.Count == rows.Count && _declared.Zip(rows).All(static p => p.First.SameAs(p.Second)))
                    return;
                throw new InvalidOperationException("段声明面已注入且内容不同：禁止第二份段声明（说明面必须单源）");
            }

            _declared = rows.ToArray();
        }
    }

    /// <summary>清除注入（测试/回退开关用）。</summary>
    public static void Reset()
    {
        lock (DeclaredGate)
        {
            _declared = null;
        }
    }

    /// <summary>按键取声明行（不存在 ⇒ null）。</summary>
    public static SegmentDeclRow? RowOf(string key)
        => Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    /// <summary>按键取固定段规格（取值委托；L0 分段族与动态段不在本表）。</summary>
    public static SegmentSpec? FixedSpecOf(string key)
        => Fixed.FirstOrDefault(s => string.Equals(s.Key.Value, key, StringComparison.Ordinal));

    /// <summary>L0 分段族模板行（未声明 ⇒ null）。</summary>
    public static SegmentDeclRow? BandTemplate
        => Rows.FirstOrDefault(static r => r.ExpandBands);

    /// <summary>段键 → 缓存带（顺序单一权威；未知键 → B5 兜底，不抛）。L0 分段族 = B0（基底前缀）。</summary>
    public static CacheBand BandFor(AnchorKey key)
    {
        if (IsL0Band(key)) return CacheBand.B0;
        foreach (var spec in Fixed)
        {
            if (spec.Key == key) return spec.Band;
        }
        return CacheBand.B5;
    }

    /// <summary>段键 → 投喂目标（未知键 → User 兜底）。L0 分段族 = System（基底前缀）。</summary>
    public static SegmentTarget TargetFor(AnchorKey key)
    {
        if (IsL0Band(key)) return SegmentTarget.System;
        foreach (var spec in Fixed)
        {
            if (spec.Key == key) return spec.Target;
        }
        return SegmentTarget.User;
    }

    /// <summary>是否 L0 分段键（<c>prompt.l0.&lt;技能名&gt;</c>）。</summary>
    public static bool IsL0Band(AnchorKey key) => key.Value.StartsWith(L0BandPrefix, StringComparison.Ordinal);

    /// <summary>L0 分段键 → 技能名。</summary>
    public static string L0BandName(AnchorKey key) => key.Value.Substring(L0BandPrefix.Length);

    /// <summary>L0 分段展示 stage（<c>prompt:l0:&lt;技能名&gt;</c>）。</summary>
    public static string L0BandStage(string name) => L0BandStagePrefix + name;


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

    /// <summary>档案开关集（按 profile 的 IncludeSkills/IncludeResources/IncludeMode/IncludeTools 归一出启用开关名）。</summary>
    public static IReadOnlyCollection<string> Toggles(bool includeSkills, bool includeMode, bool includeTools, bool includeResources = false)
    {
        var set = new List<string>(4);
        if (includeSkills) set.Add(ToggleSkills);
        if (includeMode) set.Add(ToggleMode);
        if (includeTools) set.Add(ToggleTools);
        if (includeResources) set.Add(ToggleResources);
        return set;
    }

    /// <summary>是否声明段展示 stage（声明面 = 段目录：固定段 UiStage ∪ L0 分段 stage）。</summary>
    /// <param name="stage">阶段标识（可为 null）。</param>
    public static bool IsUiStage(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage)) return false;
        var text = stage.Trim();
        foreach (var spec in Fixed)
        {
            if (string.Equals(spec.UiStage, text, StringComparison.Ordinal)) return true;
        }

        return text.StartsWith(L0BandStagePrefix, StringComparison.Ordinal);
    }

    /// <summary>段完备性判据（可机械复算）：每固定段须同时具备 UiStage/Produce/Text，且 CacheBand 序列**非降**（顺序单一权威）。</summary>
    public static IReadOnlyList<string> Validate()
    {
        var violations = new List<string>();
        CacheBand? prev = null;
        foreach (var spec in Fixed)
        {
            if (string.IsNullOrWhiteSpace(spec.UiStage)) violations.Add($"段 {spec.Key.Value} 缺 UiStage");
            if (spec.Produce is null) violations.Add($"段 {spec.Key.Value} 缺 Produce（写侧取值）");
            if (spec.Text is null) violations.Add($"段 {spec.Key.Value} 缺 Text（读侧取值）");
            if (prev.HasValue && spec.Band < prev.Value)
                violations.Add($"段 {spec.Key.Value} 的 CacheBand({spec.Band}) 低于前段({prev.Value})：顺序须按 Band 升序");
            prev = spec.Band;
        }
        return violations;
    }
}
