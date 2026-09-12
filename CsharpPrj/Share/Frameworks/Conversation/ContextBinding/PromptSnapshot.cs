using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Share.Composition;

namespace Share.Conversation.ContextBinding;

/// <summary>PromptSnapshot 的持久化 DTO（仅存储段字段；Bindings/System 为派生，反序列化时经 Builder 重建）。</summary>
internal sealed record PromptSnapshotData(
    string L0, string SkillBlock, string L1, string L2, IReadOnlyList<string> SelectedSkills);

/// <summary>
/// 写侧快照 —— provide_prompt 装配后捕获的真实 system 值（L0 / 填槽 L1 / L2 / 本轮选中 SkillBlock），
/// 供 UI 头提示层与 LLM system 共用同一份（消除「UI 用模板/重算近似」的 G1 缺口，满足 I3 哈希一致）。
/// 落 state.Data["__prompt_snapshot"]（按轮滚动，仅存本轮，防无限增长）。
/// </summary>
public sealed class PromptSnapshot
{
    public string L0 { get; init; } = string.Empty;
    public string SkillBlock { get; init; } = string.Empty;
    public string L1 { get; init; } = string.Empty;
    public string L2 { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedSkills { get; init; } = Array.Empty<string>();

    /// <summary>逐段绑定（prompt.l0/l1/l2 / skill.block），供 UI 与 LLM 双向比对。</summary>
    public IReadOnlyList<MessageBinding> Bindings { get; init; } = Array.Empty<MessageBinding>();

    /// <summary>system 槽位全文 = L0 + SkillBlock（与 <c>PromptText.Assemble(L0, SkillBlock)</c> 逐字一致）。</summary>
    public string System => TextComposer.JoinNonEmpty(new[] { L0, SkillBlock });

    public MessageBinding? BindingFor(AnchorKey key)
        => Bindings.FirstOrDefault(b => b.Key == key);

    /// <summary>序列化为 JSON（落 __prompt_snapshot 归档；仅存段字段，Bindings/System 反序列化时重建）。</summary>
    public string ToJson()
        => JsonSerializer.Serialize(new PromptSnapshotData(L0, SkillBlock, L1, L2, SelectedSkills));

    /// <summary>从 JSON 反序列化并重建 Bindings；null/空/非法 → null（无快照）。</summary>
    public static PromptSnapshot? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var d = JsonSerializer.Deserialize<PromptSnapshotData>(json);
            if (d is null) return null;
            return PromptSnapshotBuilder.Build(
                d.L0 ?? string.Empty, d.SkillBlock ?? string.Empty,
                d.L1 ?? string.Empty, d.L2 ?? string.Empty, d.SelectedSkills);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>空快照（未捕获时的兜底；BindingFor 返回 null，校验视为无 system 段）。</summary>
    public static PromptSnapshot Empty => new();
}
