using System.Collections.Generic;

namespace Plate.HttpMethod.OpenAI
{
    /// <summary>
    /// OpenAI 兼容聊天结果（通用 wire 结果，不依赖任何领域契约）。失败（网络/非 2xx/解析失败/工具循环超限/空文本）
    /// → <see cref="IsSuccess"/>=false，<see cref="Error"/> 含摘要，不静默空等。
    /// </summary>
    public sealed class OpenAiChatResult
    {
        public string? Content { get; init; }
        public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; init; }
        public string? Error { get; init; }
        public bool IsSuccess => string.IsNullOrEmpty(Error);

        public static OpenAiChatResult Ok(string? content, IReadOnlyList<OpenAiToolCall>? toolCalls = null)
            => new() { Content = content, ToolCalls = toolCalls };

        public static OpenAiChatResult Fail(string error) => new() { Error = error };
    }
}
