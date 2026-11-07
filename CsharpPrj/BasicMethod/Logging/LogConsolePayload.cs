using System;
using System.Security.Cryptography;
using System.Text;

namespace Common.Diagnostics
{
    /// <summary>
    /// 控制台日志大 payload 分级：整页 HTML 等大块文本不以原文打印，改为「长度 + 指纹」占位。
    /// <para>只换**落点**，不丢信息：内存留档与 <c>LogWritten</c> 广播仍用原文。</para>
    /// </summary>
    public static class LogConsolePayload
    {
        /// <summary>单行内联上限（= .clinerules/project.md §1.1 <c>THRESHOLD_SMALL</c>：50KB）。</summary>
        public const int MaxInlineChars = 50 * 1024;

        /// <summary>大 payload ⇒ 摘要占位；其余原样返回。</summary>
        /// <param name="message">完整格式化消息（含前缀）。</param>
        /// <param name="maxChars">内联上限（≤0 ⇒ 不折叠）。</param>
        public static string Format(string message, int maxChars = MaxInlineChars)
        {
            if (string.IsNullOrEmpty(message) || maxChars <= 0 || message.Length <= maxChars) return message;

            var trimmed = message.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '<') return message;

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)))[..8].ToLowerInvariant();
            return $"[大payload {message.Length} 字符 sha256:{hash}]";
        }
    }
}
