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
        IReadOnlyList<string>? selectedSkills)
    {
        var skills = (selectedSkills ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var bindings = new List<MessageBinding>(4);
        Add(bindings, AnchorKey.PromptL0, l0);
        Add(bindings, AnchorKey.PromptL1, l1);
        Add(bindings, AnchorKey.PromptL2, l2);
        Add(bindings, AnchorKey.SkillCombined, skillBlock);

        return new PromptSnapshot
        {
            L0 = l0 ?? string.Empty,
            SkillBlock = skillBlock ?? string.Empty,
            L1 = l1 ?? string.Empty,
            L2 = l2 ?? string.Empty,
            SelectedSkills = skills,
            Bindings = bindings,
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
