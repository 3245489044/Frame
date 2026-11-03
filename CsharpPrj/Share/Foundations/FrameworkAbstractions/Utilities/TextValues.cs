#nullable enable
using System;
using System.Collections.Generic;

namespace FrameworkAbstractions.Utilities
{
    /// <summary>
    /// 文本归一原语（**唯一实现**）：空白 = 缺失，命中值统一 <see cref="string.Trim()"/>。
    /// <para>存在理由：各处私有 <c>FirstNonEmpty</c> 对「空白」与「Trim」口径不一致，
    /// 一旦分叉即静默产生不同键值（ANTI-DUPLICATED-IMPLEMENTATION）。</para>
    /// </summary>
    public static class TextValues
    {
        /// <summary>首个非空白值（无 ⇒ null；结果 Trim）。</summary>
        public static string? FirstNonBlank(params string?[] values)
            => FirstNonBlank((IEnumerable<string?>)values);

        /// <summary>首个非空白值（无 ⇒ null；结果 Trim）。</summary>
        public static string? FirstNonBlank(IEnumerable<string?> values)
        {
            if (values is null) return null;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value!.Trim();
            }

            return null;
        }

        /// <summary>首个非空白值（无 ⇒ 空串；供既返回非空串的调用点零改形态迁移）。</summary>
        public static string FirstNonBlankOrEmpty(params string?[] values)
            => FirstNonBlank((IEnumerable<string?>)values) ?? string.Empty;

        /// <summary>按声明链取值（链序即优先级；<paramref name="read"/> 未命中 ⇒ 继续）。</summary>
        /// <param name="chain">声明链（名字序）。</param>
        /// <param name="read">名字 → 值（缺值 ⇒ null/空白）。</param>
        /// <param name="trim">命中值是否 Trim（默认 true；链式键读取需要保原值时显式 false）。</param>
        public static string? FirstByChain(IEnumerable<string> chain, Func<string, string?> read, bool trim = true)
        {
            if (chain is null || read is null) return null;
            foreach (var name in chain)
            {
                var value = read(name);
                if (string.IsNullOrWhiteSpace(value)) continue;
                return trim ? value!.Trim() : value;
            }

            return null;
        }
    }
}
