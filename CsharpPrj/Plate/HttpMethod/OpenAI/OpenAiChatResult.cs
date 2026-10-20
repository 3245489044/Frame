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

        /// <summary>provider 用量（缺字段 ⇒ null = 未观测，**禁按 0 计**）。</summary>
        public OpenAiUsage? Usage { get; init; }

        public string? Error { get; init; }
        public bool IsSuccess => string.IsNullOrEmpty(Error);

        public static OpenAiChatResult Ok(
            string? content, IReadOnlyList<OpenAiToolCall>? toolCalls = null, OpenAiUsage? usage = null)
            => new() { Content = content, ToolCalls = toolCalls, Usage = usage };

        public static OpenAiChatResult Fail(string error) => new() { Error = error };
    }

    /// <summary>
    /// provider 用量（wire 视图；全 nullable）。
    /// <para>缓存字段口径：OpenAI <c>prompt_tokens_details.cached_tokens</c>；DeepSeek <c>prompt_cache_hit_tokens</c>；
    /// Anthropic <c>cache_read_input_tokens</c> / <c>cache_creation_input_tokens</c>。</para>
    /// </summary>
    public sealed record OpenAiUsage(
        int? PromptTokens,
        int? CompletionTokens,
        int? CacheReadTokens,
        int? CacheWriteTokens);
}

