using System.Text;

namespace Plate.JsonMethod.Utilities
{
    /// <summary>
    /// 原文层宽松修复（**仅作严格解析失败后的兜底**；机制只修复，不裁决是否采用）：
    /// ① 字符串字面量内的未转义反斜杠（Windows 路径等）—— JSON 转义序列仅 <c>" \ / b f n r t u</c> 合法，
    ///    路径中的 <c>\P</c> 一类会让**整篇**请求被判非法，故须兜住"字符串里出现符号"这一脆性。
    /// ② 根值闭合后的尾随噪声（多余引号 / 说明文字）。
    /// <para>
    /// 判定口径（为何"同一串内一律加倍"而非只补非法位）：<c>D:\temp\Project</c> 的报错由 <c>\P</c> 触发，
    /// 但 <c>\t</c> 是**合法**转义（会被解析成 TAB）——只补 <c>\P</c> 会静默留下被吞掉的 TAB，
    /// 比直接报错更糟。故某串一旦出现非法转义，即证明它是"裸文本串"，整串反斜杠一律加倍。
    /// </para>
    /// <para>
    /// 边界（如实登记，不在覆盖范围）：纯合法转义串（如 <c>"C:\temp"</c>）严格解析本就成功，不会触发本兜底，
    /// 无法与"真想写 TAB"区分——该情形不产生解析失败，需另行决策（路径字段主动规范化）。
    /// </para>
    /// </summary>
    public static class JsonEscapeRepair
    {
        /// <summary>JSON 合法转义引导字符（<c>u</c> 另需 4 位十六进制，见 <see cref="HasFourHexDigits"/>）。</summary>
        private const string EscapeStarts = "\"\\/bfnrtu";

        /// <summary>
        /// 兜底修复（先裁尾随噪声，再修反斜杠——尾部噪声里的引号会干扰字面量扫描）。
        /// </summary>
        /// <param name="json">原始 JSON 文本。</param>
        /// <param name="repaired">修复结果（无改动 = 原文）。</param>
        /// <param name="report">命中明细。</param>
        /// <returns>是否有任何改动。</returns>
        public static bool TryRepair(string json, out string repaired, out JsonRepairReport report)
        {
            ArgumentNullException.ThrowIfNull(json);

            var current = json;
            var trimmedChars = 0;
            var escapeHits = 0;

            if (TryTrimTrailingNoise(current, out var afterTrim, out var removed))
            {
                current = afterTrim;
                trimmedChars = removed;
            }
            if (TryRepairUnescapedBackslashes(current, out var afterEscape, out var hits))
            {
                current = afterEscape;
                escapeHits = hits;
            }

            repaired = current;
            report = new JsonRepairReport(escapeHits, trimmedChars);
            return report.Changed;
        }

        /// <summary>
        /// 加倍字符串字面量内的未转义反斜杠（串外反斜杠本就是非法 JSON，原样留给解析器报错，不做猜测）。
        /// </summary>
        /// <param name="json">原始 JSON 文本。</param>
        /// <param name="repaired">修复结果（无改动 = 原文）。</param>
        /// <param name="hits">被加倍的反斜杠处数。</param>
        /// <returns>是否有改动。</returns>
        public static bool TryRepairUnescapedBackslashes(string json, out string repaired, out int hits)
        {
            ArgumentNullException.ThrowIfNull(json);

            repaired = json;
            hits = 0;
            if (json.Length == 0) return false;

            var buffer = new StringBuilder(json.Length + 16);
            var changed = false;
            var cursor = 0;
            while (cursor < json.Length)
            {
                if (json[cursor] != '"')
                {
                    buffer.Append(json[cursor]);
                    cursor++;
                    continue;
                }

                var literalEnd = ScanStringLiteral(json, cursor, out var hasIllegalEscape);
                if (!hasIllegalEscape)
                {
                    buffer.Append(json, cursor, literalEnd - cursor);
                }
                else
                {
                    // 非法转义的字符串整体按裸文本解读：串内反斜杠一律加倍（上方 summary 给出理由）。
                    for (var i = cursor; i < literalEnd; i++)
                    {
                        if (json[i] != '\\')
                        {
                            buffer.Append(json[i]);
                            continue;
                        }
                        buffer.Append('\\').Append('\\');
                        hits++;
                        changed = true;
                    }
                }
                cursor = literalEnd;
            }

            if (!changed) return false;
            repaired = buffer.ToString();
            return true;
        }

        /// <summary>
        /// 截断根容器闭合之后的尾随噪声（多余引号 / 说明文字）。
        /// </summary>
        /// <param name="json">原始 JSON 文本。</param>
        /// <param name="trimmed">截断结果（无改动 = 原文）。</param>
        /// <param name="removed">被截断的字符数。</param>
        /// <returns>是否有改动。</returns>
        public static bool TryTrimTrailingNoise(string json, out string trimmed, out int removed)
        {
            ArgumentNullException.ThrowIfNull(json);

            trimmed = json;
            removed = 0;

            var rootEnd = FindRootValueEnd(json);
            if (rootEnd <= 0 || rootEnd >= json.Length) return false;
            if (json.AsSpan(rootEnd).Trim().Length == 0) return false;

            trimmed = json[..rootEnd];
            removed = json.Length - rootEnd;
            return true;
        }

        /// <summary>根容器闭合位的后一位；无完整根容器（含非容器根 / 结构已坏）→ -1。</summary>
        private static int FindRootValueEnd(string json)
        {
            var depth = 0;
            var started = false;
            for (var cursor = 0; cursor < json.Length; cursor++)
            {
                var ch = json[cursor];
                if (ch == '"')
                {
                    cursor = ScanStringLiteral(json, cursor, out _) - 1;
                    continue;
                }
                if (ch is '{' or '[')
                {
                    depth++;
                    started = true;
                    continue;
                }
                if (ch is not ('}' or ']')) continue;

                depth--;
                if (depth < 0) return -1;                 // 闭合多于开括号：结构已坏，不猜测
                if (started && depth == 0) return cursor + 1;
            }
            return -1;                                    // 未闭合：交给重解析裁决
        }

        /// <summary>从起始引号扫到终止引号之后一位；未闭合 → 吃到文本末尾（由重解析裁决）。</summary>
        /// <param name="json">原始 JSON 文本。</param>
        /// <param name="quoteIndex">起始引号偏移。</param>
        /// <param name="hasIllegalEscape">串内是否存在非法转义序列。</param>
        private static int ScanStringLiteral(string json, int quoteIndex, out bool hasIllegalEscape)
        {
            hasIllegalEscape = false;
            var cursor = quoteIndex + 1;
            while (cursor < json.Length)
            {
                var ch = json[cursor];
                if (ch == '"') return cursor + 1;
                if (ch != '\\')
                {
                    cursor++;
                    continue;
                }

                var escapeStart = cursor + 1 < json.Length ? json[cursor + 1] : '\0';
                if (EscapeStarts.IndexOf(escapeStart) < 0) hasIllegalEscape = true;
                else if (escapeStart == 'u' && !HasFourHexDigits(json, cursor + 2)) hasIllegalEscape = true;

                cursor += 2;
            }
            return json.Length;
        }

        /// <summary><c>\u</c> 后是否恰有 4 位十六进制（不足或非十六进制 → 该转义非法）。</summary>
        private static bool HasFourHexDigits(string json, int start)
        {
            if (start + 4 > json.Length) return false;
            for (var i = start; i < start + 4; i++)
                if (!Uri.IsHexDigit(json[i])) return false;
            return true;
        }
    }
}
