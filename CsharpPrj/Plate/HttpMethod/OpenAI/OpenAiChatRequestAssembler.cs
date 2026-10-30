using System.Collections.Generic;

namespace Plate.HttpMethod.OpenAI;

/// <summary>
/// 通用聊天消息装配：system + 历史 + user → wire DTO。
/// <para>只吃原始值（role/content 文本元组），不引任何领域消息类型；领域适配层（如 Agent 的
/// <c>LlmMessage</c>）只需投影为元组后调用本装配，消除各插件各写一份 <c>BuildMessages</c>。</para>
/// </summary>
public static class OpenAiChatRequestAssembler
{
    /// <summary>装配消息序列：非空 system 在前，历史居中，非空 user 在后。</summary>
    public static List<OpenAiChatMessage> Build(
        string? system,
        string? user,
        IEnumerable<(string Role, string? Content)>? history = null)
    {
        var list = new List<OpenAiChatMessage>();
        if (!string.IsNullOrWhiteSpace(system)) list.Add(new OpenAiChatMessage("system", system));
        if (history is not null)
            foreach (var (role, content) in history)
                list.Add(new OpenAiChatMessage(role, content));
        if (!string.IsNullOrWhiteSpace(user)) list.Add(new OpenAiChatMessage("user", user));
        return list;
    }
}
