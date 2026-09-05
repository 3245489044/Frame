using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Conversation.ContextBinding;

/// <summary>校验规则标识（对齐方案 §10.1 R1–R6）。</summary>
public enum BindingRule
{
    /// <summary>UI ⊆ LLM：UI 不出现 LLM 输入之外的任何消息（R1）。</summary>
    R1_UI_SubsetOf_LLM,

    /// <summary>LLM ⊆ UI：LLM 喂的每一条都显示（R2）。</summary>
    R2_LLM_SubsetOf_UI,

    /// <summary>system 逐字：LlmSystem[prompt.*] ≡ UiView[prompt.*] ≡ __prompt_snapshot（哈希一致）（R3）。</summary>
    R3_SystemExact,

    /// <summary>键唯一：AnchorKey 全局唯一，无重复（R4）。</summary>
    R4_KeyUnique,

    /// <summary>锚点可解析：AnchorKey 能被 SplitWfKey/已知前缀解析，无悬空（R5）。</summary>
    R5_KeyResolvable,

    /// <summary>零额外：UI 不存在 Feed∉{LlmHistory,LlmSystem} 或未绑定 LLM 输入的消息（R6）。</summary>
    R6_ZeroExtra,
}

/// <summary>单条校验违规（{rule, anchorKey, source, detail} → 差异报告 / __context_binding 落盘）。</summary>
public sealed record BindingViolation(BindingRule Rule, string AnchorKey, string Source, string Detail)
{
    public override string ToString() => $"[{Rule}] {Source} anchorKey='{AnchorKey}' {Detail}";
}

/// <summary>
/// 上下文绑定校验器（P3，纯函数、零 Agent 依赖）—— 以「UI ≡ LLM 双向逐字相等 + 零额外逻辑」为硬性不变量，
/// 依 R1–R6 对 UI 视图消息序列（<paramref name="uiView"/>）与 LLM 输入消息序列（<paramref name="llmView"/>）
/// 以及写侧快照（<paramref name="promptSnapshot"/>，UI 与 LLM system 共用同一份）做一致性校验。
/// </summary>
public static class ContextBindingValidator
{
    /// <summary>双向校验；无违规返回空列表（全绿）。</summary>
    public static IReadOnlyList<BindingViolation> Validate(
        IReadOnlyList<ConversationMessage>? uiView,
        IReadOnlyList<ConversationMessage>? llmView,
        PromptSnapshot? promptSnapshot)
    {
        // R6 需检查全量 UI（含未绑定消息）；R1-R5 仅对已绑定消息（带 Binding）生效。
        var uiAll = uiView ?? Array.Empty<ConversationMessage>();
        var llmAll = llmView ?? Array.Empty<ConversationMessage>();
        var ui = uiAll.Where(m => m.Binding is not null).ToList();
        var llm = llmAll.Where(m => m.Binding is not null).ToList();

        var violations = new List<BindingViolation>();
        CheckR6(violations, uiAll);
        CheckR4(violations, ui, "Ui");
        CheckR4(violations, llm, "Llm");
        CheckR5(violations, ui, "Ui");
        CheckR5(violations, llm, "Llm");
        CheckSubset(violations, ui, llm, BindingRule.R1_UI_SubsetOf_LLM, "Ui");
        CheckSubset(violations, llm, ui, BindingRule.R2_LLM_SubsetOf_UI, "Llm");
        CheckR3(violations, ui, llm, promptSnapshot);
        return violations;
    }

    /// <summary>R4：视图内 AnchorKey 唯一。</summary>
    private static void CheckR4(List<BindingViolation> violations, List<ConversationMessage> msgs, string source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in msgs)
        {
            var key = m.Binding!.Key.Value;
            if (!seen.Add(key))
                violations.Add(new BindingViolation(BindingRule.R4_KeyUnique, key, source, "AnchorKey 重复"));
        }
    }

    /// <summary>R5：AnchorKey 可解析（非空 + 已知前缀合法 / SplitWfKey 可拆）。</summary>
    private static void CheckR5(List<BindingViolation> violations, List<ConversationMessage> msgs, string source)
    {
        foreach (var m in msgs)
        {
            var key = m.Binding!.Key.Value;
            if (!TryResolve(key, out var detail))
                violations.Add(new BindingViolation(BindingRule.R5_KeyResolvable, key, source, detail));
        }
    }

    /// <summary>R6：UI 每条消息都带 Binding 且 Origin ∈ {LlmHistory, LlmSystem}（无 UiOnly / 未绑定）。</summary>
    private static void CheckR6(List<BindingViolation> violations, IReadOnlyList<ConversationMessage> ui)
    {
        foreach (var m in ui)
        {
            if (m.Binding is null)
                violations.Add(new BindingViolation(BindingRule.R6_ZeroExtra, string.Empty, "Ui", "未绑定 LLM 输入的消息"));
            else if (m.Binding.Origin is not (MessageOrigin.LlmHistory or MessageOrigin.LlmSystem))
                violations.Add(new BindingViolation(BindingRule.R6_ZeroExtra, m.Binding.Key.Value, "Ui", "Origin 不在 {LlmHistory,LlmSystem}"));
        }
    }

    /// <summary>R1/R2：集合逐条匹配（同 AnchorKey 且文本哈希相等）。</summary>
    private static void CheckSubset(
        List<BindingViolation> violations,
        List<ConversationMessage> sub,
        List<ConversationMessage> sup,
        BindingRule rule,
        string source)
    {
        // 反集（sup）按 key → 文本哈希集合索引
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var s in sup)
        {
            var key = s.Binding!.Key.Value;
            if (!index.TryGetValue(key, out var set))
                index[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(PromptSnapshotBuilder.Hash(s.Text));
        }

        foreach (var m in sub)
        {
            var key = m.Binding!.Key.Value;
            var hash = PromptSnapshotBuilder.Hash(m.Text);
            if (!index.TryGetValue(key, out var set) || !set.Contains(hash))
                violations.Add(new BindingViolation(rule, key, source, "消息仅出现在一侧（键或文本不匹配）"));
        }
    }

    /// <summary>R3：system 逐字 —— prompt.* 段哈希与快照一致（UI 与 LLM 均来自同一 __prompt_snapshot）。</summary>
    private static void CheckR3(
        List<BindingViolation> violations,
        List<ConversationMessage> ui,
        List<ConversationMessage> llm,
        PromptSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Bindings.Count == 0) return;

        var uiHash = HashByKey(ui);
        var llmHash = HashByKey(llm);

        foreach (var b in snapshot.Bindings)
        {
            if (b.Origin != MessageOrigin.LlmSystem) continue;
            var key = b.Key.Value;
            // LLM system 段哈希须等于快照
            if (!llmHash.TryGetValue(key, out var lh) || lh != b.TextHash)
                violations.Add(new BindingViolation(BindingRule.R3_SystemExact, key, "Llm", "system 段与 __prompt_snapshot 不一致"));
            // UI 头提示层段哈希须等于快照
            if (!uiHash.TryGetValue(key, out var uh) || uh != b.TextHash)
                violations.Add(new BindingViolation(BindingRule.R3_SystemExact, key, "Ui", "头提示层与 __prompt_snapshot 不一致"));
        }
    }

    private static Dictionary<string, string> HashByKey(List<ConversationMessage> msgs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in msgs)
        {
            var key = m.Binding!.Key.Value;
            map[key] = PromptSnapshotBuilder.Hash(m.Text);
        }
        return map;
    }

    /// <summary>锚点可解析判定：非空，且要么为已知前缀（history.{seq}/prompt.l*/skill.*），要么为 SplitWfKey 可拆的节点键。</summary>
    private static bool TryResolve(string key, out string detail)
    {
        if (string.IsNullOrEmpty(key))
        {
            detail = "AnchorKey 为空（悬空）";
            return false;
        }
        if (key.StartsWith("history.", StringComparison.Ordinal))
        {
            var seq = key.Substring("history.".Length);
            var ok = int.TryParse(seq, out _);
            detail = ok ? string.Empty : $"history 后缀非数字: '{seq}'";
            return ok;
        }
        if (key.StartsWith("prompt.l", StringComparison.Ordinal)
            || key.StartsWith("skill.", StringComparison.Ordinal))
        {
            detail = string.Empty;
            return true;
        }
        // 节点键：SplitWfKey 拆出 (wf, node)，至少 node 非空（有或没有 wf 前缀均可）
        SplitWfKey(key, out _, out var node);
        if (string.IsNullOrEmpty(node))
        {
            detail = "节点键无法解析（node 为空）";
            return false;
        }
        detail = string.Empty;
        return true;
    }

    /// <summary>拆分节点键（与 ConversationScenarioProjector / ConversationViewService 同构）：“wf.nodeKey” → (“wf”,“nodeKey”)；无前缀 → (“”, key)。</summary>
    private static void SplitWfKey(string key, out string wf, out string node)
    {
        var dot = key.IndexOf('.');
        if (dot > 0)
        {
            wf = key.Substring(0, dot);
            node = key.Substring(dot + 1);
        }
        else
        {
            wf = string.Empty;
            node = key;
        }
    }
}
