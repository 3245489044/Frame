using System.Collections.Generic;

namespace Plate.HttpMethod.OpenAI
{
    /// <summary>
    /// OpenAI 兼容聊天消息（通用 wire DTO，不依赖任何领域契约）。工具循环回填：
    /// assistant 携带 <see cref="ToolCalls"/>，tool 携带 <see cref="ToolCallId"/>。
    /// </summary>
    public sealed class OpenAiChatMessage
    {
        public string Role { get; set; } = "user";
        public string? Content { get; set; }
        public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; set; }
        public string? ToolCallId { get; set; }

        public OpenAiChatMessage() { }

        public OpenAiChatMessage(string role, string? content)
        {
            Role = role;
            Content = content;
        }
    }
}
