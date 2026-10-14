using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 行选择集纯运算（无槽、无 DOM、无 IO）——「行身份编码 + 翻转/全选/反选/规范化/等值」的通用件，
    /// 供任意表格/列表页的勾选语义复用（便于单测锁定翻转/全选/反选/空集拒绝语义）。
    /// <para>通用件：零领域类型；行身份 = <c>domain␟key</c>（域 = 行来源域，同键跨域可辨），分隔符为 U+001F。</para>
    /// </summary>
    public static class RowSelection
    {
        /// <summary>行身份分隔符（U+001F，<c>domain␟key</c>；域 = 行来源域，读身份 = 写/删身份）。</summary>
        public const char DomainSeparator = '\u001f';

        /// <summary>行身份编码（<c>domain␟key</c>）；key 空 ⇒ <c>null</c>（不可勾选）。</summary>
        public static string? Encode(string? domain, string? key)
            => string.IsNullOrWhiteSpace(key)
                ? null
                : (domain?.Trim() ?? string.Empty) + DomainSeparator + key!.Trim();

        /// <summary>解码行身份；无分隔符（裸键）⇒ domain 回退 <paramref name="fallbackDomain"/>（兼容旧调用）。</summary>
        public static (string Domain, string Key) Decode(string? entry, string fallbackDomain)
        {
            if (string.IsNullOrEmpty(entry)) return (fallbackDomain, string.Empty);
            var i = entry!.IndexOf(DomainSeparator);
            return i < 0
                ? (fallbackDomain, entry.Trim())
                : (entry.Substring(0, i), entry.Substring(i + 1).Trim());
        }

        /// <summary>翻转单键（命中则移出，未命中则加入）。</summary>
        public static void Toggle(ISet<string> set, string? key)
        {
            if (set is null || string.IsNullOrWhiteSpace(key)) return;
            var k = key!.Trim();
            if (!set.Remove(k)) set.Add(k);
        }

        /// <summary>全选（以当前域键集为准，先清后填）。</summary>
        public static void SelectAll(ISet<string> set, IEnumerable<string>? keys)
        {
            if (set is null) return;
            set.Clear();
            foreach (var k in Normalize(keys)) set.Add(k);
        }

        /// <summary>反选（在当前域键集内翻转，域外键剔除）。</summary>
        public static void Invert(ISet<string> set, IEnumerable<string>? keys)
        {
            if (set is null) return;
            var domain = Normalize(keys);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in domain)
            {
                if (!set.Contains(k)) keep.Add(k);
            }

            set.Clear();
            foreach (var k in keep) set.Add(k);
        }

        /// <summary>清空。</summary>
        public static void Clear(ISet<string> set) => set?.Clear();

        /// <summary>键集规范化（trim + 去空 + 去重保序）。</summary>
        public static IReadOnlyList<string> Normalize(IEnumerable<string>? keys)
            => keys is null
                ? Array.Empty<string>()
                : keys.Where(k => !string.IsNullOrWhiteSpace(k))
                      .Select(k => k.Trim())
                      .Distinct(StringComparer.Ordinal)
                      .ToList();

        /// <summary>两键集是否等值（序无关）。</summary>
        public static bool SetEquals(IReadOnlyCollection<string>? a, IReadOnlyCollection<string>? b)
        {
            if (a is null || b is null) return ReferenceEquals(a, b);
            if (a.Count != b.Count) return false;
            var set = new HashSet<string>(a, StringComparer.Ordinal);
            return set.SetEquals(b);
        }
    }
}
